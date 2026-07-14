namespace StreamDecky.Integrations.MicMixer;

public interface IMicMixerClient : IDisposable, IAsyncDisposable
{
    event EventHandler<MicMixerConnectionState>? ConnectionStateChanged;
    event EventHandler<MicMixerMusicState>? StateChanged;

    MicMixerConnectionState ConnectionState { get; }
    bool IsConnected { get; }
    MicMixerHello? ServerInfo { get; }
    MicMixerMusicState? State { get; }

    void Start();
    Task StopAsync();

    Task<MicMixerMusicState> GetStateAsync(CancellationToken cancellationToken = default);
    Task<MicMixerTrackPage> GetTracksAsync(
        string? search = null,
        string? folderId = null,
        int offset = 0,
        int limit = 200,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MicMixerMusicFolder>> GetFoldersAsync(CancellationToken cancellationToken = default);
    Task RefreshLibraryAsync(CancellationToken cancellationToken = default);
    Task AddMusicFolderAsync(string path, CancellationToken cancellationToken = default);
    Task RemoveMusicFolderAsync(string folderId, CancellationToken cancellationToken = default);
    Task ResetMusicFoldersAsync(CancellationToken cancellationToken = default);
    Task SetDownloadFolderAsync(string folderId, CancellationToken cancellationToken = default);
    Task SwitchToLibraryModeAsync(CancellationToken cancellationToken = default);
    Task DownloadFromUrlAsync(
        string url,
        string? folderId = null,
        CancellationToken cancellationToken = default);
    Task PlayTrackAsync(string trackId, CancellationToken cancellationToken = default);
    Task TogglePlayPauseAsync(CancellationToken cancellationToken = default);
    Task StopPlaybackAsync(CancellationToken cancellationToken = default);
    Task PreviousAsync(CancellationToken cancellationToken = default);
    Task NextAsync(CancellationToken cancellationToken = default);
    Task SeekAsync(double positionSeconds, CancellationToken cancellationToken = default);
    Task SetMusicVolumeAsync(double volume, CancellationToken cancellationToken = default);
    Task SetMonitorVolumeAsync(double volume, CancellationToken cancellationToken = default);
    Task SetVolumesLinkedAsync(bool linked, CancellationToken cancellationToken = default);
    Task EnqueueTrackAsync(string trackId, CancellationToken cancellationToken = default);
    Task RemoveQueueItemAsync(int index, CancellationToken cancellationToken = default);
    Task MoveQueueItemAsync(int fromIndex, int toIndex, CancellationToken cancellationToken = default);
    Task ClearQueueAsync(CancellationToken cancellationToken = default);
    Task StartDelayedPlayAsync(string? trackId = null, CancellationToken cancellationToken = default);
    Task CancelDelayedPlayAsync(CancellationToken cancellationToken = default);
    Task SetDelayedStartSecondsAsync(int seconds, CancellationToken cancellationToken = default);
    Task SetSingleTrackModeAsync(MicMixerSingleTrackMode mode, CancellationToken cancellationToken = default);
}
