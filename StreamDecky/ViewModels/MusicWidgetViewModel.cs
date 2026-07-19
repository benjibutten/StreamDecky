using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamDecky.Helpers;
using StreamDecky.Integrations.MicMixer;

namespace StreamDecky.ViewModels;

/// <summary>
/// Drives the MicMixer music widget in the overlay. Owns a control-pipe client
/// that keeps reconnecting in the background, mirrors pushed player state into
/// bindable properties, and exposes the transport/library/queue commands.
/// </summary>
public partial class MusicWidgetViewModel : ObservableObject, IDisposable
{
    public const int TrackPageSize = 200;
    private const int SearchDebounceMilliseconds = 275;

    private readonly IMicMixerClient _client;
    private readonly bool _ownsClient;
    private bool _isActivated;
    private bool _isDisposed;

    private string? _loadedLibraryVersion;
    private string? _loadedQueueVersion;
    private int _searchVersion;
    private int _tracksRequestVersion;
    private int _loadedTrackOffset;
    private string _folderSignature = string.Empty;
    private string? _selectedTrackId;
    private bool _isPaused;

    private bool _suppressVolumeCommands;
    private double? _pendingMusicVolume;
    private bool _musicVolumeSendInFlight;
    private double? _pendingMonitorVolume;
    private bool _monitorVolumeSendInFlight;

    private double _musicVolumePercent = 50;
    private double _monitorVolumePercent = 50;

    public MusicWidgetViewModel(IMicMixerClient? client = null)
    {
        _ownsClient = client == null;
        _client = client ?? new MicMixerClient();
        _client.ConnectionStateChanged += OnConnectionStateChanged;
        _client.StateChanged += OnClientStateChanged;
        UpdateConnectionProperties(_client.ConnectionState);
    }

    public ObservableCollection<MusicTrackItemViewModel> Tracks { get; } = new();
    public ObservableCollection<MusicQueueItemViewModel> Queue { get; } = new();
    public ObservableCollection<MusicFolderOptionViewModel> Folders { get; } = new();

    [ObservableProperty]
    private MicMixerConnectionState _connectionState = MicMixerConnectionState.Stopped;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatusText = "Waiting for MicMixer…";

    [ObservableProperty]
    private bool _showRetryButton;

    [ObservableProperty]
    private bool _showPlayerControls;

    [ObservableProperty]
    private bool _isExternalMode;

    [ObservableProperty]
    private string _nowPlayingText = "No track selected";

    [ObservableProperty]
    private bool _hasCurrentTrack;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private double _positionSeconds;

    [ObservableProperty]
    private double _durationSeconds;

    [ObservableProperty]
    private string _trackTimeText = "0:00 / 0:00";

    [ObservableProperty]
    private bool _hasMonitorOutput;

    [ObservableProperty]
    private bool _canStartPlayback;

    [ObservableProperty]
    private string _playbackBlockedReason = string.Empty;

    [ObservableProperty]
    private bool _showRoutingHint;

    [ObservableProperty]
    private bool _isDelayedStartActive;

    [ObservableProperty]
    private int _delayedStartSeconds = 3;

    [ObservableProperty]
    private string _delayedPlayText = "3 s";

    [ObservableProperty]
    private string _singleTrackModeText = "Off";

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasQueue;

    [ObservableProperty]
    private string _queueCountText = "Queue";

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoadingTracks;

    [ObservableProperty]
    private bool _hasTracks;

    [ObservableProperty]
    private bool _hasMoreTracks;

    [ObservableProperty]
    private string _trackCountText = string.Empty;

    [ObservableProperty]
    private bool _showFolderFilter;

    [ObservableProperty]
    private bool _volumesLinked;

    [ObservableProperty]
    private bool _musicIgnoresPushToTalk;

    [ObservableProperty]
    private bool _musicMonitorOnly;

    /// <summary>Whether the connected MicMixer advertises the musicRouting capability.
    /// Older versions reject the routing commands as unknown, so the toggles are
    /// hidden entirely when it is missing.</summary>
    [ObservableProperty]
    private bool _supportsMusicRouting;

    [ObservableProperty]
    private bool _hasSelectedTrack;

    [ObservableProperty]
    private string _delayedPlayToolTip = DefaultDelayedPlayToolTip;

    private const string DefaultDelayedPlayToolTip =
        "Delayed start — the music begins after the countdown so you can alt-tab back to the game. " +
        "Select a track in the library to arm it specifically. Click again to cancel.";

    /// <summary>Set by the overlay while the user drags the seek slider so pushed
    /// state updates do not yank the thumb around mid-drag.</summary>
    public bool IsSeekDragging { get; set; }

    public string? SelectedFolderId { get; private set; }

    public double MusicVolumePercent
    {
        get => _musicVolumePercent;
        set
        {
            double clamped = Math.Clamp(value, 0, 100);
            if (Math.Abs(_musicVolumePercent - clamped) < 0.05)
                return;

            _musicVolumePercent = clamped;
            OnPropertyChanged();
            if (!_suppressVolumeCommands)
                SendMusicVolume(clamped / 100.0);
        }
    }

    public double MonitorVolumePercent
    {
        get => _monitorVolumePercent;
        set
        {
            double clamped = Math.Clamp(value, 0, 100);
            if (Math.Abs(_monitorVolumePercent - clamped) < 0.05)
                return;

            _monitorVolumePercent = clamped;
            OnPropertyChanged();
            if (!_suppressVolumeCommands)
                SendMonitorVolume(clamped / 100.0);
        }
    }

    /// <summary>Starts the reconnect loop the first time the widget is shown. The
    /// client only tries a handful of times before giving up, so re-activation
    /// (e.g. reopening the overlay) also restarts a loop that gave up.</summary>
    public void Activate()
    {
        if (_isDisposed)
            return;

        if (!_isActivated || ConnectionState == MicMixerConnectionState.Unavailable)
        {
            _isActivated = true;
            _client.Start();
        }
    }

    [RelayCommand]
    private void RetryConnect() => Activate();

    private void OnConnectionStateChanged(object? sender, MicMixerConnectionState state)
    {
        UpdateConnectionProperties(state);

        if (state == MicMixerConnectionState.Connected)
        {
            _loadedLibraryVersion = null;
            _loadedQueueVersion = null;
            ErrorMessage = string.Empty;
            _ = RefreshAfterConnectAsync();
        }
    }

    private void UpdateConnectionProperties(MicMixerConnectionState state)
    {
        ConnectionState = state;
        IsConnected = state == MicMixerConnectionState.Connected;
        // The client completes the hello handshake (which carries ServerInfo)
        // before it reports Connected, so the capability check is safe here.
        SupportsMusicRouting = IsConnected
            && _client.ServerInfo?.Capabilities.Contains("musicRouting") == true;
        ShowRetryButton = state == MicMixerConnectionState.Unavailable;
        ConnectionStatusText = state switch
        {
            MicMixerConnectionState.Connected => "Connected to MicMixer",
            MicMixerConnectionState.Connecting or MicMixerConnectionState.Disconnected => "Looking for MicMixer…",
            MicMixerConnectionState.Unavailable => "MicMixer wasn't found. Start it, then press Try again.",
            _ => "Start MicMixer and the widget connects automatically."
        };
        ShowPlayerControls = IsConnected && !IsExternalMode;
    }

    private async Task RefreshAfterConnectAsync()
    {
        try
        {
            MicMixerMusicState state = await _client.GetStateAsync();
            ApplyState(state);
        }
        catch (Exception ex)
        {
            // The pushed stateChanged events keep the widget alive even if this
            // initial fetch loses a race with a disconnect.
            AppDiagnostics.Info($"MicMixer widget initial state fetch failed: {ex.Message}");
        }
    }

    private void OnClientStateChanged(object? sender, MicMixerMusicState state)
    {
        ApplyState(state);
    }

    private void ApplyState(MicMixerMusicState state)
    {
        IsExternalMode = state.IsExternalMode;
        ShowPlayerControls = IsConnected && !state.IsExternalMode;

        HasCurrentTrack = !string.IsNullOrWhiteSpace(state.CurrentTrackName);
        NowPlayingText = HasCurrentTrack ? state.CurrentTrackName! : "No track selected";
        IsPlaying = string.Equals(state.PlaybackState, "Playing", StringComparison.OrdinalIgnoreCase);
        _isPaused = string.Equals(state.PlaybackState, "Paused", StringComparison.OrdinalIgnoreCase);

        if (!IsSeekDragging)
        {
            DurationSeconds = Math.Max(0, state.DurationSeconds);
            PositionSeconds = Math.Clamp(state.PositionSeconds, 0, Math.Max(0, state.DurationSeconds));
        }

        TrackTimeText = $"{FormatTime(state.PositionSeconds)} / {FormatTime(state.DurationSeconds)}";

        _suppressVolumeCommands = true;
        try
        {
            if (!_musicVolumeSendInFlight && _pendingMusicVolume == null)
                MusicVolumePercent = state.MusicVolume * 100.0;

            if (!_monitorVolumeSendInFlight && _pendingMonitorVolume == null)
                MonitorVolumePercent = state.MonitorVolume * 100.0;
        }
        finally
        {
            _suppressVolumeCommands = false;
        }

        HasMonitorOutput = state.HasMonitorOutput;
        CanStartPlayback = state.CanStartPlayback;
        // External mode has its own actionable banner above the player. Repeating
        // the playback gate's machine-readable reason below it is redundant.
        PlaybackBlockedReason = state.CanStartPlayback || state.IsExternalMode
            ? string.Empty
            : state.PlaybackBlockedReason ?? string.Empty;
        ShowRoutingHint = !state.IsRouting && !state.IsExternalMode;

        IsDelayedStartActive = state.IsDelayedStartActive;
        DelayedStartSeconds = state.DelayedStartSeconds;
        DelayedPlayText = state.IsDelayedStartActive
            ? $"{Math.Max(0, state.DelayedStartRemainingSeconds)} s…"
            : $"{state.DelayedStartSeconds} s";

        SingleTrackModeText = state.SingleTrackMode;
        StatusText = state.StatusText;
        VolumesLinked = state.VolumesLinked;
        MusicIgnoresPushToTalk = state.MusicIgnoresPushToTalk;
        MusicMonitorOnly = state.MusicMonitorOnly;

        if (!string.Equals(_loadedQueueVersion, state.QueueVersion, StringComparison.Ordinal))
        {
            _loadedQueueVersion = state.QueueVersion;
            RebuildQueue(state.Queue);
        }

        UpdateFolders(state.Folders);
        UpdateTrackHighlights(state);

        if (!string.Equals(_loadedLibraryVersion, state.LibraryVersion, StringComparison.Ordinal))
        {
            _loadedLibraryVersion = state.LibraryVersion;
            _ = ReloadTracksAsync();
        }
    }

    private void RebuildQueue(IReadOnlyList<MicMixerQueueItem> queue)
    {
        Queue.Clear();
        foreach (MicMixerQueueItem item in queue)
            Queue.Add(new MusicQueueItemViewModel(item.Index, item.TrackId, item.Name));

        HasQueue = Queue.Count > 0;
        QueueCountText = Queue.Count == 1 ? "Queue · 1 track" : $"Queue · {Queue.Count} tracks";
    }

    private void UpdateFolders(IReadOnlyList<MicMixerMusicFolder> folders)
    {
        // Rebuild only when the set changes; the chip list is bound and churn
        // would reset hover/selection visuals four times a second.
        string signature = string.Join("", folders.Select(f => $"{f.Id}{f.Name}"));
        if (string.Equals(_folderSignature, signature, StringComparison.Ordinal))
            return;

        _folderSignature = signature;

        if (SelectedFolderId != null && folders.All(f => !string.Equals(f.Id, SelectedFolderId, StringComparison.Ordinal)))
            SelectedFolderId = null;

        Folders.Clear();
        Folders.Add(new MusicFolderOptionViewModel(null, "All") { IsSelected = SelectedFolderId == null });
        foreach (MicMixerMusicFolder folder in folders)
        {
            Folders.Add(new MusicFolderOptionViewModel(folder.Id, folder.Name)
            {
                IsSelected = string.Equals(folder.Id, SelectedFolderId, StringComparison.Ordinal)
            });
        }

        ShowFolderFilter = folders.Count > 1;
    }

    private void UpdateTrackHighlights(MicMixerMusicState state)
    {
        var queuePositionsByTrack = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (MicMixerQueueItem item in state.Queue)
        {
            if (!queuePositionsByTrack.TryGetValue(item.TrackId, out List<int>? positions))
            {
                positions = new List<int>();
                queuePositionsByTrack[item.TrackId] = positions;
            }

            positions.Add(item.Index + 1);
        }

        foreach (MusicTrackItemViewModel track in Tracks)
        {
            track.IsCurrent = string.Equals(track.Id, state.CurrentTrackId, StringComparison.Ordinal);
            track.QueueBadgeText = queuePositionsByTrack.TryGetValue(track.Id, out List<int>? positions)
                ? $"Queued #{string.Join(", #", positions)}"
                : string.Empty;
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        _ = DebounceReloadTracksAsync();
    }

    private async Task DebounceReloadTracksAsync()
    {
        int version = ++_searchVersion;
        await Task.Delay(SearchDebounceMilliseconds);
        if (version == _searchVersion)
            await ReloadTracksAsync();
    }

    private async Task ReloadTracksAsync()
    {
        if (!_client.IsConnected)
            return;

        int version = ++_tracksRequestVersion;
        IsLoadingTracks = true;
        try
        {
            string? search = string.IsNullOrWhiteSpace(SearchQuery) ? null : SearchQuery.Trim();
            MicMixerTrackPage page = await _client.GetTracksAsync(search, SelectedFolderId, 0, TrackPageSize);
            if (version != _tracksRequestVersion)
                return;

            Tracks.Clear();
            foreach (MicMixerTrack track in page.Tracks)
                Tracks.Add(CreateTrackItem(track));

            _loadedTrackOffset = page.Tracks.Count;
            UpdateTrackPagingProperties(page.Total);
            RefreshTrackHighlightsFromClientState();
        }
        catch (Exception ex)
        {
            if (version == _tracksRequestVersion)
                ReportCommandError(ex);
        }
        finally
        {
            if (version == _tracksRequestVersion)
                IsLoadingTracks = false;
        }
    }

    [RelayCommand]
    private async Task LoadMoreTracksAsync()
    {
        if (!_client.IsConnected || IsLoadingTracks)
            return;

        int version = ++_tracksRequestVersion;
        IsLoadingTracks = true;
        try
        {
            string? search = string.IsNullOrWhiteSpace(SearchQuery) ? null : SearchQuery.Trim();
            MicMixerTrackPage page = await _client.GetTracksAsync(search, SelectedFolderId, _loadedTrackOffset, TrackPageSize);
            if (version != _tracksRequestVersion)
                return;

            foreach (MicMixerTrack track in page.Tracks)
                Tracks.Add(CreateTrackItem(track));

            _loadedTrackOffset += page.Tracks.Count;
            UpdateTrackPagingProperties(page.Total);
            RefreshTrackHighlightsFromClientState();
        }
        catch (Exception ex)
        {
            if (version == _tracksRequestVersion)
                ReportCommandError(ex);
        }
        finally
        {
            if (version == _tracksRequestVersion)
                IsLoadingTracks = false;
        }
    }

    private MusicTrackItemViewModel CreateTrackItem(MicMixerTrack track)
    {
        return new MusicTrackItemViewModel(track.Id, track.Name, track.FolderName)
        {
            IsCurrent = track.IsPlaying,
            IsSelected = string.Equals(track.Id, _selectedTrackId, StringComparison.Ordinal),
            QueueBadgeText = track.QueuePositions.Count > 0
                ? $"Queued #{string.Join(", #", track.QueuePositions)}"
                : string.Empty
        };
    }

    private void UpdateTrackPagingProperties(int total)
    {
        HasTracks = Tracks.Count > 0;
        HasMoreTracks = Tracks.Count < total;
        TrackCountText = HasMoreTracks
            ? $"{Tracks.Count} of {total} tracks"
            : total == 1 ? "1 track" : $"{total} tracks";
    }

    private void RefreshTrackHighlightsFromClientState()
    {
        if (_client.State is { } state)
            UpdateTrackHighlights(state);
    }

    [RelayCommand]
    private void SelectFolder(MusicFolderOptionViewModel? folder)
    {
        if (folder == null)
            return;

        SelectedFolderId = folder.Id;
        foreach (MusicFolderOptionViewModel option in Folders)
            option.IsSelected = ReferenceEquals(option, folder);

        _ = ReloadTracksAsync();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = string.Empty;
    }

    /// <summary>Marks a track as the target for the play and delayed-play buttons;
    /// clicking the selected track again deselects it.</summary>
    [RelayCommand]
    private void ToggleTrackSelection(MusicTrackItemViewModel? track)
    {
        if (track == null)
            return;

        _selectedTrackId = string.Equals(track.Id, _selectedTrackId, StringComparison.Ordinal)
            ? null
            : track.Id;

        foreach (MusicTrackItemViewModel item in Tracks)
            item.IsSelected = string.Equals(item.Id, _selectedTrackId, StringComparison.Ordinal);

        HasSelectedTrack = _selectedTrackId != null;
        DelayedPlayToolTip = _selectedTrackId != null
            ? $"Delayed start — plays \"{track.Name}\" after the countdown. Click again to cancel."
            : DefaultDelayedPlayToolTip;
    }

    [RelayCommand]
    private Task TogglePlayPauseAsync()
    {
        // Match MicMixer's play button: when fully stopped, start the marked track
        // instead of the server-side default; pause/resume otherwise.
        if (!IsPlaying && !_isPaused && _selectedTrackId is string selectedTrackId)
            return RunAsync(() => _client.PlayTrackAsync(selectedTrackId));

        return RunAsync(() => _client.TogglePlayPauseAsync());
    }

    [RelayCommand]
    private Task StopPlaybackAsync() => RunAsync(() => _client.StopPlaybackAsync());

    [RelayCommand]
    private Task PreviousAsync() => RunAsync(() => _client.PreviousAsync());

    [RelayCommand]
    private Task NextAsync() => RunAsync(() => _client.NextAsync());

    [RelayCommand]
    private Task PlayTrackAsync(MusicTrackItemViewModel? track) =>
        track == null ? Task.CompletedTask : RunAsync(() => _client.PlayTrackAsync(track.Id));

    [RelayCommand]
    private Task EnqueueTrackAsync(MusicTrackItemViewModel? track) =>
        track == null ? Task.CompletedTask : RunAsync(() => _client.EnqueueTrackAsync(track.Id));

    [RelayCommand]
    private Task RemoveQueueItemAsync(MusicQueueItemViewModel? item) =>
        item == null ? Task.CompletedTask : RunAsync(() => _client.RemoveQueueItemAsync(item.Index));

    [RelayCommand]
    private Task MoveQueueItemUpAsync(MusicQueueItemViewModel? item) =>
        item == null || item.Index <= 0
            ? Task.CompletedTask
            : RunAsync(() => _client.MoveQueueItemAsync(item.Index, item.Index - 1));

    [RelayCommand]
    private Task MoveQueueItemDownAsync(MusicQueueItemViewModel? item) =>
        item == null || item.Index >= Queue.Count - 1
            ? Task.CompletedTask
            : RunAsync(() => _client.MoveQueueItemAsync(item.Index, item.Index + 1));

    [RelayCommand]
    private Task ClearQueueAsync() => RunAsync(() => _client.ClearQueueAsync());

    [RelayCommand]
    private Task ToggleDelayedPlayAsync() =>
        RunAsync(() => IsDelayedStartActive
            ? _client.CancelDelayedPlayAsync()
            : _client.StartDelayedPlayAsync(_selectedTrackId));

    [RelayCommand]
    private Task ToggleVolumesLinkedAsync() =>
        RunAsync(() => _client.SetVolumesLinkedAsync(!VolumesLinked));

    [RelayCommand]
    private Task ToggleMusicIgnoresPushToTalkAsync() =>
        RunAsync(() => _client.SetMusicIgnoresPushToTalkAsync(!MusicIgnoresPushToTalk));

    [RelayCommand]
    private Task ToggleMusicMonitorOnlyAsync() =>
        RunAsync(() => _client.SetMusicMonitorOnlyAsync(!MusicMonitorOnly));

    [RelayCommand]
    private Task SetDelayedStartSecondsAsync(object? parameter)
    {
        int seconds = parameter switch
        {
            int value => value,
            string text when int.TryParse(text, out int parsed) => parsed,
            _ => 0
        };

        return seconds <= 0 ? Task.CompletedTask : RunAsync(() => _client.SetDelayedStartSecondsAsync(seconds));
    }

    [RelayCommand]
    private Task CycleSingleTrackModeAsync()
    {
        MicMixerSingleTrackMode next = SingleTrackModeText switch
        {
            "Once" => MicMixerSingleTrackMode.Always,
            "Always" => MicMixerSingleTrackMode.Off,
            _ => MicMixerSingleTrackMode.Once
        };

        return RunAsync(() => _client.SetSingleTrackModeAsync(next));
    }

    [RelayCommand]
    private Task SwitchToLibraryModeAsync() => RunAsync(() => _client.SwitchToLibraryModeAsync());

    [RelayCommand]
    private Task RefreshLibraryAsync() => RunAsync(() => _client.RefreshLibraryAsync());

    public Task SeekToAsync(double positionSeconds) =>
        RunAsync(() => _client.SeekAsync(Math.Max(0, positionSeconds)));

    private async void SendMusicVolume(double volume)
    {
        _pendingMusicVolume = volume;
        if (_musicVolumeSendInFlight || !_client.IsConnected)
        {
            if (!_client.IsConnected)
                _pendingMusicVolume = null;
            return;
        }

        _musicVolumeSendInFlight = true;
        try
        {
            while (_pendingMusicVolume is double next)
            {
                _pendingMusicVolume = null;
                await _client.SetMusicVolumeAsync(next);
            }
        }
        catch (Exception ex)
        {
            _pendingMusicVolume = null;
            ReportCommandError(ex);
        }
        finally
        {
            _musicVolumeSendInFlight = false;
        }
    }

    private async void SendMonitorVolume(double volume)
    {
        _pendingMonitorVolume = volume;
        if (_monitorVolumeSendInFlight || !_client.IsConnected)
        {
            if (!_client.IsConnected)
                _pendingMonitorVolume = null;
            return;
        }

        _monitorVolumeSendInFlight = true;
        try
        {
            while (_pendingMonitorVolume is double next)
            {
                _pendingMonitorVolume = null;
                await _client.SetMonitorVolumeAsync(next);
            }
        }
        catch (Exception ex)
        {
            _pendingMonitorVolume = null;
            ReportCommandError(ex);
        }
        finally
        {
            _monitorVolumeSendInFlight = false;
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (!_client.IsConnected)
            return;

        try
        {
            ErrorMessage = string.Empty;
            await action();
        }
        catch (Exception ex)
        {
            ReportCommandError(ex);
        }
    }

    private void ReportCommandError(Exception exception)
    {
        switch (exception)
        {
            case MicMixerCommandException commandException:
                ErrorMessage = commandException.Message;
                break;
            case OperationCanceledException:
                break;
            case InvalidOperationException or IOException or TimeoutException:
                ErrorMessage = "Lost the connection to MicMixer.";
                break;
            default:
                ErrorMessage = "The MicMixer command failed.";
                AppDiagnostics.Warning("MicMixer widget command failed unexpectedly.", exception);
                break;
        }
    }

    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            seconds = 0;

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _client.ConnectionStateChanged -= OnConnectionStateChanged;
        _client.StateChanged -= OnClientStateChanged;

        if (_ownsClient)
            _client.Dispose();
    }
}

public partial class MusicTrackItemViewModel : ObservableObject
{
    public MusicTrackItemViewModel(string id, string name, string folderName)
    {
        Id = id;
        Name = name;
        FolderName = folderName;
    }

    public string Id { get; }
    public string Name { get; }
    public string FolderName { get; }

    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _queueBadgeText = string.Empty;
}

public sealed class MusicQueueItemViewModel
{
    public MusicQueueItemViewModel(int index, string trackId, string name)
    {
        Index = index;
        TrackId = trackId;
        Name = name;
    }

    public int Index { get; }
    public string TrackId { get; }
    public string Name { get; }
    public string NumberText => $"{Index + 1}.";
}

public partial class MusicFolderOptionViewModel : ObservableObject
{
    public MusicFolderOptionViewModel(string? id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>Null represents the "All folders" option.</summary>
    public string? Id { get; }
    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;
}
