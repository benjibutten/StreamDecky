using StreamDecky.Integrations.MicMixer;
using StreamDecky.ViewModels;
using Xunit;

namespace StreamDecky.Tests;

public sealed class MusicWidgetViewModelTests
{
    [Fact]
    public void Activate_ShouldStartTheClientOnce()
    {
        var client = new FakeMicMixerClient();
        using var viewModel = new MusicWidgetViewModel(client);

        viewModel.Activate();
        viewModel.Activate();

        Assert.Equal(1, client.StartCallCount);
    }

    [Fact]
    public void Disconnected_ShouldReportWaitingStatus()
    {
        var client = new FakeMicMixerClient();
        using var viewModel = new MusicWidgetViewModel(client);

        client.RaiseConnectionState(MicMixerConnectionState.Disconnected);

        Assert.False(viewModel.IsConnected);
        Assert.False(viewModel.ShowPlayerControls);
        Assert.Contains("MicMixer", viewModel.ConnectionStatusText);
    }

    [Fact]
    public void Unavailable_ShouldOfferRetry_AndRetryRestartsTheClient()
    {
        var client = new FakeMicMixerClient();
        using var viewModel = new MusicWidgetViewModel(client);
        viewModel.Activate();

        client.RaiseConnectionState(MicMixerConnectionState.Unavailable);

        Assert.True(viewModel.ShowRetryButton);
        Assert.Contains("Try again", viewModel.ConnectionStatusText);

        viewModel.RetryConnectCommand.Execute(null);
        Assert.Equal(2, client.StartCallCount);

        // Reopening the overlay re-activates the widget, which also restarts a
        // client that gave up.
        viewModel.Activate();
        Assert.Equal(3, client.StartCallCount);
    }

    [Fact]
    public void Connect_ShouldLoadStateAndTracks()
    {
        var client = new FakeMicMixerClient();
        client.NextState = CreateState() with
        {
            CurrentTrackId = "t1",
            CurrentTrackName = "Track one",
            PlaybackState = "Playing",
            Queue = new[] { new MicMixerQueueItem(0, "t2", "Track two") }
        };
        client.NextTrackPage = new MicMixerTrackPage(0, 200, 2, "lib-1", new[]
        {
            CreateTrack("t1", "Track one"),
            CreateTrack("t2", "Track two")
        });

        using var viewModel = new MusicWidgetViewModel(client);
        client.RaiseConnected();

        Assert.True(viewModel.IsConnected);
        Assert.True(viewModel.ShowPlayerControls);
        Assert.Equal("Track one", viewModel.NowPlayingText);
        Assert.True(viewModel.IsPlaying);
        Assert.Equal(2, viewModel.Tracks.Count);
        Assert.True(viewModel.Tracks[0].IsCurrent);
        Assert.Equal("Queued #1", viewModel.Tracks[1].QueueBadgeText);
        Assert.Single(viewModel.Queue);
        Assert.True(viewModel.HasQueue);
    }

    [Fact]
    public void StateChanged_ShouldUpdateDelayedPlayAndSingleTrackMode()
    {
        var client = new FakeMicMixerClient();
        using var viewModel = new MusicWidgetViewModel(client);
        client.RaiseConnected();

        client.RaiseState(CreateState() with
        {
            IsDelayedStartActive = true,
            DelayedStartSeconds = 5,
            DelayedStartRemainingSeconds = 2,
            SingleTrackMode = "Once"
        });

        Assert.True(viewModel.IsDelayedStartActive);
        Assert.Equal("2 s…", viewModel.DelayedPlayText);
        Assert.Equal("Once", viewModel.SingleTrackModeText);
    }

    [Fact]
    public void StateChanged_WithNewLibraryVersion_ShouldReloadTracks()
    {
        var client = new FakeMicMixerClient();
        using var viewModel = new MusicWidgetViewModel(client);
        client.RaiseConnected();
        int loadsAfterConnect = client.GetTracksCallCount;

        client.NextTrackPage = new MicMixerTrackPage(0, 200, 1, "lib-2", new[]
        {
            CreateTrack("t9", "New track")
        });
        client.RaiseState(CreateState() with { LibraryVersion = "lib-2" });

        Assert.Equal(loadsAfterConnect + 1, client.GetTracksCallCount);
        Assert.Single(viewModel.Tracks);
        Assert.Equal("New track", viewModel.Tracks[0].Name);
    }

    [Fact]
    public void TransportCommands_ShouldSendClientCommands()
    {
        var client = new FakeMicMixerClient();
        using var viewModel = new MusicWidgetViewModel(client);
        client.RaiseConnected();

        viewModel.TogglePlayPauseCommand.Execute(null);
        viewModel.StopPlaybackCommand.Execute(null);
        viewModel.PreviousCommand.Execute(null);
        viewModel.NextCommand.Execute(null);
        viewModel.ToggleDelayedPlayCommand.Execute(null);
        viewModel.SetDelayedStartSecondsCommand.Execute("5");
        viewModel.CycleSingleTrackModeCommand.Execute(null);

        Assert.Contains("togglePlayPause", client.Commands);
        Assert.Contains("stop", client.Commands);
        Assert.Contains("previous", client.Commands);
        Assert.Contains("next", client.Commands);
        Assert.Contains("startDelayedPlay", client.Commands);
        Assert.Contains("setDelayedStartSeconds:5", client.Commands);
        Assert.Contains("setSingleTrackMode:Once", client.Commands);
    }

    [Fact]
    public void SelectedTrack_ShouldArmDelayedPlayAndPlayButton()
    {
        var client = new FakeMicMixerClient();
        client.NextTrackPage = new MicMixerTrackPage(0, 200, 2, "lib-1", new[]
        {
            CreateTrack("t1", "Track one"),
            CreateTrack("t2", "Track two")
        });
        using var viewModel = new MusicWidgetViewModel(client);
        client.RaiseConnected();

        viewModel.ToggleTrackSelectionCommand.Execute(viewModel.Tracks[1]);

        Assert.True(viewModel.HasSelectedTrack);
        Assert.True(viewModel.Tracks[1].IsSelected);
        Assert.False(viewModel.Tracks[0].IsSelected);
        Assert.Contains("Track two", viewModel.DelayedPlayToolTip);

        viewModel.ToggleDelayedPlayCommand.Execute(null);
        Assert.Contains("startDelayedPlay:t2", client.Commands);

        // The play button starts the marked track when playback is fully stopped.
        viewModel.TogglePlayPauseCommand.Execute(null);
        Assert.Contains("playTrack:t2", client.Commands);
    }

    [Fact]
    public void ToggleTrackSelection_OnTheSelectedTrack_ShouldDeselect()
    {
        var client = new FakeMicMixerClient();
        client.NextTrackPage = new MicMixerTrackPage(0, 200, 1, "lib-1", new[] { CreateTrack("t1", "Track one") });
        using var viewModel = new MusicWidgetViewModel(client);
        client.RaiseConnected();

        viewModel.ToggleTrackSelectionCommand.Execute(viewModel.Tracks[0]);
        viewModel.ToggleTrackSelectionCommand.Execute(viewModel.Tracks[0]);

        Assert.False(viewModel.HasSelectedTrack);
        Assert.False(viewModel.Tracks[0].IsSelected);

        viewModel.ToggleDelayedPlayCommand.Execute(null);
        Assert.Contains("startDelayedPlay", client.Commands);
        Assert.DoesNotContain("startDelayedPlay:t1", client.Commands);
    }

    [Fact]
    public void VolumesLinked_ShouldFollowStateAndToggleCommand()
    {
        var client = new FakeMicMixerClient();
        using var viewModel = new MusicWidgetViewModel(client);
        client.RaiseConnected();

        client.RaiseState(CreateState() with { VolumesLinked = true });
        Assert.True(viewModel.VolumesLinked);

        viewModel.ToggleVolumesLinkedCommand.Execute(null);
        Assert.Contains("setVolumesLinked:False", client.Commands);
    }

    [Fact]
    public void MusicRouting_ShouldBeSupported_WhenTheServerAdvertisesTheCapability()
    {
        var client = new FakeMicMixerClient();
        using var viewModel = new MusicWidgetViewModel(client);

        Assert.False(viewModel.SupportsMusicRouting);

        client.RaiseConnected();
        Assert.True(viewModel.SupportsMusicRouting);

        client.RaiseConnectionState(MicMixerConnectionState.Disconnected);
        Assert.False(viewModel.SupportsMusicRouting);
    }

    [Fact]
    public void MusicRouting_ShouldBeHidden_AgainstAnOlderMicMixer()
    {
        var client = new FakeMicMixerClient
        {
            ServerInfo = new MicMixerHello("MicMixer", "0.9", 1, new[] { "state", "transport" })
        };
        using var viewModel = new MusicWidgetViewModel(client);
        client.RaiseConnected();

        Assert.False(viewModel.SupportsMusicRouting);
    }

    [Fact]
    public void MusicRoutingToggles_ShouldFollowStateAndToggleCommands()
    {
        var client = new FakeMicMixerClient();
        using var viewModel = new MusicWidgetViewModel(client);
        client.RaiseConnected();

        client.RaiseState(CreateState() with { MusicIgnoresPushToTalk = true, MusicMonitorOnly = true });
        Assert.True(viewModel.MusicIgnoresPushToTalk);
        Assert.True(viewModel.MusicMonitorOnly);

        viewModel.ToggleMusicIgnoresPushToTalkCommand.Execute(null);
        Assert.Contains("setMusicIgnoresPushToTalk:False", client.Commands);

        viewModel.ToggleMusicMonitorOnlyCommand.Execute(null);
        Assert.Contains("setMusicMonitorOnly:False", client.Commands);
    }

    [Fact]
    public void CommandsWhileDisconnected_ShouldBeIgnored()
    {
        var client = new FakeMicMixerClient();
        using var viewModel = new MusicWidgetViewModel(client);

        viewModel.TogglePlayPauseCommand.Execute(null);

        Assert.Empty(client.Commands);
    }

    [Fact]
    public void MusicVolumeChange_ShouldSendVolumeCommand()
    {
        var client = new FakeMicMixerClient();
        using var viewModel = new MusicWidgetViewModel(client);
        client.RaiseConnected();

        viewModel.MusicVolumePercent = 80;

        Assert.Contains("setMusicVolume:0.8", client.Commands);
    }

    [Fact]
    public void FailedCommand_ShouldSurfaceTheServerMessage()
    {
        var client = new FakeMicMixerClient
        {
            CommandException = new MicMixerCommandException("track_not_found", "The track does not exist.")
        };
        using var viewModel = new MusicWidgetViewModel(client);
        client.RaiseConnected();

        viewModel.NextCommand.Execute(null);

        Assert.Equal("The track does not exist.", viewModel.ErrorMessage);
    }

    [Fact]
    public void ExternalMode_ShouldHidePlayerControls()
    {
        var client = new FakeMicMixerClient();
        using var viewModel = new MusicWidgetViewModel(client);
        client.RaiseConnected();

        client.RaiseState(CreateState() with { IsExternalMode = true });

        Assert.True(viewModel.IsExternalMode);
        Assert.False(viewModel.ShowPlayerControls);
    }

    private static MicMixerTrack CreateTrack(string id, string name) =>
        new(id, name, "f1", "Music", @"C:\Music", false, Array.Empty<int>());

    private static MicMixerMusicState CreateState() => new(
        PlaybackState: "Stopped",
        IsExternalMode: false,
        CurrentTrackId: null,
        CurrentTrackName: null,
        PositionSeconds: 0,
        DurationSeconds: 0,
        MusicVolume: 0.5,
        MonitorVolume: 0.5,
        IsRouting: true,
        HasMonitorOutput: true,
        CanStartPlayback: true,
        PlaybackBlockedReason: null,
        IsDelayedStartActive: false,
        DelayedStartSeconds: 3,
        DelayedStartRemainingSeconds: 0,
        SingleTrackMode: "Off",
        LibraryVersion: "lib-1",
        QueueVersion: "q-1",
        Folders: new[] { new MicMixerMusicFolder("f1", "Music", @"C:\Music", true, false, true) },
        Queue: Array.Empty<MicMixerQueueItem>(),
        IsDownloading: false,
        DownloadPercent: null,
        DownloadStatus: string.Empty,
        StatusText: string.Empty);

    private sealed class FakeMicMixerClient : IMicMixerClient
    {
        public List<string> Commands { get; } = new();
        public int StartCallCount { get; private set; }
        public int GetTracksCallCount { get; private set; }
        public MicMixerMusicState NextState { get; set; } = CreateState();
        public MicMixerTrackPage NextTrackPage { get; set; } =
            new(0, 200, 0, "lib-1", Array.Empty<MicMixerTrack>());
        public Exception? CommandException { get; set; }

        public event EventHandler<MicMixerConnectionState>? ConnectionStateChanged;
        public event EventHandler<MicMixerMusicState>? StateChanged;

        public MicMixerConnectionState ConnectionState { get; private set; } = MicMixerConnectionState.Stopped;
        public bool IsConnected => ConnectionState == MicMixerConnectionState.Connected;

        /// <summary>Mirrors the real client: populated by the hello handshake before
        /// Connected is raised. Defaults to a current server advertising musicRouting.</summary>
        public MicMixerHello? ServerInfo { get; set; } =
            new("MicMixer", "1.0", 1, new[] { "state", "transport", "musicRouting" });

        public MicMixerMusicState? State { get; private set; }

        public void RaiseConnected()
        {
            State = NextState;
            RaiseConnectionState(MicMixerConnectionState.Connected);
        }

        public void RaiseConnectionState(MicMixerConnectionState state)
        {
            ConnectionState = state;
            ConnectionStateChanged?.Invoke(this, state);
        }

        public void RaiseState(MicMixerMusicState state)
        {
            State = state;
            StateChanged?.Invoke(this, state);
        }

        public void Start() => StartCallCount++;

        public Task StopAsync() => Task.CompletedTask;

        public Task<MicMixerMusicState> GetStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(State ?? NextState);

        public Task<MicMixerTrackPage> GetTracksAsync(
            string? search = null,
            string? folderId = null,
            int offset = 0,
            int limit = 200,
            CancellationToken cancellationToken = default)
        {
            GetTracksCallCount++;
            return Task.FromResult(NextTrackPage);
        }

        public Task<IReadOnlyList<MicMixerMusicFolder>> GetFoldersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MicMixerMusicFolder>>(Array.Empty<MicMixerMusicFolder>());

        public Task RefreshLibraryAsync(CancellationToken cancellationToken = default) => Record("refreshLibrary");
        public Task AddMusicFolderAsync(string path, CancellationToken cancellationToken = default) => Record($"addMusicFolder:{path}");
        public Task RemoveMusicFolderAsync(string folderId, CancellationToken cancellationToken = default) => Record($"removeMusicFolder:{folderId}");
        public Task ResetMusicFoldersAsync(CancellationToken cancellationToken = default) => Record("resetMusicFolders");
        public Task SetDownloadFolderAsync(string folderId, CancellationToken cancellationToken = default) => Record($"setDownloadFolder:{folderId}");
        public Task SwitchToLibraryModeAsync(CancellationToken cancellationToken = default) => Record("switchToLibraryMode");
        public Task DownloadFromUrlAsync(string url, string? folderId = null, CancellationToken cancellationToken = default) => Record($"downloadFromUrl:{url}");
        public Task PlayTrackAsync(string trackId, CancellationToken cancellationToken = default) => Record($"playTrack:{trackId}");
        public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default) => Record("togglePlayPause");
        public Task StopPlaybackAsync(CancellationToken cancellationToken = default) => Record("stop");
        public Task PreviousAsync(CancellationToken cancellationToken = default) => Record("previous");
        public Task NextAsync(CancellationToken cancellationToken = default) => Record("next");
        public Task SeekAsync(double positionSeconds, CancellationToken cancellationToken = default) => Record($"seek:{positionSeconds}");

        public Task SetMusicVolumeAsync(double volume, CancellationToken cancellationToken = default) =>
            Record($"setMusicVolume:{volume.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        public Task SetMonitorVolumeAsync(double volume, CancellationToken cancellationToken = default) =>
            Record($"setMonitorVolume:{volume.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        public Task SetVolumesLinkedAsync(bool linked, CancellationToken cancellationToken = default) =>
            Record($"setVolumesLinked:{linked}");

        public Task SetMusicIgnoresPushToTalkAsync(bool enabled, CancellationToken cancellationToken = default) =>
            Record($"setMusicIgnoresPushToTalk:{enabled}");

        public Task SetMusicMonitorOnlyAsync(bool enabled, CancellationToken cancellationToken = default) =>
            Record($"setMusicMonitorOnly:{enabled}");

        public Task EnqueueTrackAsync(string trackId, CancellationToken cancellationToken = default) => Record($"enqueueTrack:{trackId}");
        public Task RemoveQueueItemAsync(int index, CancellationToken cancellationToken = default) => Record($"removeQueueItem:{index}");
        public Task MoveQueueItemAsync(int fromIndex, int toIndex, CancellationToken cancellationToken = default) => Record($"moveQueueItem:{fromIndex}->{toIndex}");
        public Task ClearQueueAsync(CancellationToken cancellationToken = default) => Record("clearQueue");

        public Task StartDelayedPlayAsync(string? trackId = null, CancellationToken cancellationToken = default) =>
            Record(trackId == null ? "startDelayedPlay" : $"startDelayedPlay:{trackId}");
        public Task CancelDelayedPlayAsync(CancellationToken cancellationToken = default) => Record("cancelDelayedPlay");
        public Task SetDelayedStartSecondsAsync(int seconds, CancellationToken cancellationToken = default) => Record($"setDelayedStartSeconds:{seconds}");
        public Task SetSingleTrackModeAsync(MicMixerSingleTrackMode mode, CancellationToken cancellationToken = default) => Record($"setSingleTrackMode:{mode}");

        private Task Record(string command)
        {
            if (CommandException != null)
                return Task.FromException(CommandException);

            Commands.Add(command);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
