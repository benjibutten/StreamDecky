namespace StreamDecky.Integrations.MicMixer;

public enum MicMixerConnectionState
{
    Stopped,
    Disconnected,
    Connecting,
    Connected,

    /// <summary>The reconnect loop exhausted its attempts and stopped; call
    /// <see cref="IMicMixerClient.Start"/> again to retry.</summary>
    Unavailable
}

public enum MicMixerSingleTrackMode
{
    Off,
    Once,
    Always
}

public sealed record MicMixerHello(
    string Product,
    string Version,
    int ProtocolVersion,
    IReadOnlyList<string> Capabilities);

public sealed record MicMixerMusicState(
    string PlaybackState,
    bool IsExternalMode,
    string? CurrentTrackId,
    string? CurrentTrackName,
    double PositionSeconds,
    double DurationSeconds,
    double MusicVolume,
    double MonitorVolume,
    bool IsRouting,
    bool HasMonitorOutput,
    bool CanStartPlayback,
    string? PlaybackBlockedReason,
    bool IsDelayedStartActive,
    int DelayedStartSeconds,
    int DelayedStartRemainingSeconds,
    string SingleTrackMode,
    string LibraryVersion,
    string QueueVersion,
    IReadOnlyList<MicMixerMusicFolder> Folders,
    IReadOnlyList<MicMixerQueueItem> Queue,
    bool IsDownloading,
    double? DownloadPercent,
    string DownloadStatus,
    string StatusText,
    bool VolumesLinked = false);

public sealed record MicMixerTrack(
    string Id,
    string Name,
    string FolderId,
    string FolderName,
    string FolderPath,
    bool IsPlaying,
    IReadOnlyList<int> QueuePositions);

public sealed record MicMixerTrackPage(
    int Offset,
    int Limit,
    int Total,
    string LibraryVersion,
    IReadOnlyList<MicMixerTrack> Tracks);

public sealed record MicMixerQueueItem(int Index, string TrackId, string Name);

public sealed record MicMixerMusicFolder(
    string Id,
    string Name,
    string Path,
    bool IsDefault,
    bool IsPreferredDownloadFolder,
    bool IsEffectiveDownloadFolder);

public sealed class MicMixerCommandException : Exception
{
    public MicMixerCommandException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
