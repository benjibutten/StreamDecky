using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreamDecky.Helpers;

namespace StreamDecky.Integrations.MicMixer;

public sealed class MicMixerClient : IMicMixerClient
{
    public const int ProtocolVersion = 1;
    public const string DefaultPipeName = "MicMixer.Control.v1";
    public const int DefaultMaxConsecutiveConnectFailures = 5;

    private const int MaximumMessageCharacters = 1_048_576;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _pipeName;
    private readonly int _maxConsecutiveConnectFailures;
    private readonly SynchronizationContext? _eventContext;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<WireEnvelope>> _pending = new();
    private readonly object _lifecycleLock = new();

    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _runTask;
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private int _disposed;
    private int _connectionState = (int)MicMixerConnectionState.Stopped;
    private MicMixerMusicState? _state;

    public MicMixerClient(
        string? pipeName = null,
        SynchronizationContext? eventContext = null,
        int maxConsecutiveConnectFailures = DefaultMaxConsecutiveConnectFailures)
    {
        _pipeName = pipeName ?? DefaultPipeName;
        _eventContext = eventContext ?? SynchronizationContext.Current;
        _maxConsecutiveConnectFailures = maxConsecutiveConnectFailures;
    }

    public event EventHandler<MicMixerConnectionState>? ConnectionStateChanged;
    public event EventHandler<MicMixerMusicState>? StateChanged;

    public MicMixerConnectionState ConnectionState => (MicMixerConnectionState)Volatile.Read(ref _connectionState);
    public bool IsConnected => ConnectionState == MicMixerConnectionState.Connected;
    public MicMixerHello? ServerInfo { get; private set; }
    public MicMixerMusicState? State => Volatile.Read(ref _state);

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        lock (_lifecycleLock)
        {
            if (_runTask is { IsCompleted: false })
            {
                return;
            }

            _lifetimeCancellation?.Dispose();
            _lifetimeCancellation = new CancellationTokenSource();
            _runTask = Task.Run(() => RunReconnectLoopAsync(_lifetimeCancellation.Token));
        }
    }

    public async Task StopAsync()
    {
        Task? runTask;
        lock (_lifecycleLock)
        {
            _lifetimeCancellation?.Cancel();
            runTask = _runTask;
        }

        if (runTask != null)
        {
            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        SetConnectionState(MicMixerConnectionState.Stopped);
    }

    private async Task RunReconnectLoopAsync(CancellationToken cancellationToken)
    {
        int retryDelayMilliseconds = 250;
        int consecutiveConnectFailures = 0;
        bool unavailableWasLogged = false;
        SetConnectionState(MicMixerConnectionState.Disconnected);

        while (!cancellationToken.IsCancellationRequested)
        {
            SetConnectionState(MicMixerConnectionState.Connecting);
            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.ConnectAsync(1_000, cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

                _pipe = pipe;
                _writer = writer;
                Task readTask = ReadLoopAsync(reader, cancellationToken);

                ServerInfo = await SendRequestAsync<MicMixerHello>(
                    "hello",
                    payload: null,
                    requireConnectedState: false,
                    cancellationToken).ConfigureAwait(false);

                if (ServerInfo.ProtocolVersion != ProtocolVersion)
                {
                    throw new MicMixerCommandException(
                        "unsupported_protocol",
                        $"MicMixer uses control protocol {ServerInfo.ProtocolVersion}; StreamDecky supports {ProtocolVersion}.");
                }

                retryDelayMilliseconds = 250;
                consecutiveConnectFailures = 0;
                unavailableWasLogged = false;
                SetConnectionState(MicMixerConnectionState.Connected);
                await readTask.ConfigureAwait(false);
                throw new IOException("MicMixer closed the control connection.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!unavailableWasLogged)
                {
                    unavailableWasLogged = true;
                    if (ex is IOException or TimeoutException or MicMixerCommandException)
                    {
                        AppDiagnostics.Info($"MicMixer control connection is unavailable: {ex.Message}");
                    }
                    else
                    {
                        AppDiagnostics.Warning("MicMixer control connection failed unexpectedly.", ex);
                    }
                }
            }
            finally
            {
                _writer = null;
                _pipe = null;
                ServerInfo = null;
                FailPending(new IOException("The MicMixer control connection was closed."));
            }

            consecutiveConnectFailures++;
            if (_maxConsecutiveConnectFailures > 0
                && consecutiveConnectFailures >= _maxConsecutiveConnectFailures)
            {
                // Stop hunting for MicMixer; a new Start() call restarts the loop.
                AppDiagnostics.Info(
                    $"MicMixer was not found after {consecutiveConnectFailures} attempts; giving up until the next Start().");
                SetConnectionState(MicMixerConnectionState.Unavailable);
                return;
            }

            SetConnectionState(MicMixerConnectionState.Disconnected);
            try
            {
                await Task.Delay(retryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            retryDelayMilliseconds = Math.Min(retryDelayMilliseconds * 2, 5_000);
        }
    }

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var lineReader = new BoundedLineReader(reader, MaximumMessageCharacters);
        while (!cancellationToken.IsCancellationRequested)
        {
            BoundedLine line = await lineReader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (line.EndOfStream)
            {
                return;
            }

            if (line.TooLarge)
            {
                throw new IOException("MicMixer sent an oversized control message.");
            }

            WireEnvelope envelope = JsonSerializer.Deserialize<WireEnvelope>(line.Value!, JsonOptions)
                ?? throw new IOException("MicMixer sent an empty control message.");

            if (string.Equals(envelope.Type, "response", StringComparison.OrdinalIgnoreCase))
            {
                if (envelope.Id != null && _pending.TryRemove(envelope.Id, out TaskCompletionSource<WireEnvelope>? pending))
                {
                    pending.TrySetResult(envelope);
                }

                continue;
            }

            if (string.Equals(envelope.Type, "event", StringComparison.OrdinalIgnoreCase)
                && envelope.Data is JsonElement { ValueKind: JsonValueKind.Object } eventData
                && eventData.TryGetProperty("name", out JsonElement name)
                && string.Equals(name.GetString(), "stateChanged", StringComparison.Ordinal)
                && eventData.TryGetProperty("state", out JsonElement stateElement))
            {
                MicMixerMusicState? state = stateElement.Deserialize<MicMixerMusicState>(JsonOptions);
                if (state != null)
                {
                    Volatile.Write(ref _state, state);
                    RaiseOnEventContext(() => StateChanged?.Invoke(this, state));
                }
            }
        }
    }

    private async Task<T> SendRequestAsync<T>(
        string command,
        object? payload,
        bool requireConnectedState,
        CancellationToken cancellationToken)
    {
        if (requireConnectedState && !IsConnected)
        {
            throw new InvalidOperationException("MicMixer is not connected.");
        }

        StreamWriter writer = _writer
            ?? throw new InvalidOperationException("MicMixer is not connected.");
        string id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<WireEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException("Could not allocate a MicMixer request id.");
        }

        try
        {
            string json = JsonSerializer.Serialize(
                new WireRequest(id, command, payload, ProtocolVersion),
                JsonOptions);

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }

            WireEnvelope response = await completion.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken).ConfigureAwait(false);

            if (response.Success != true)
            {
                throw new MicMixerCommandException(
                    response.Error?.Code ?? "command_failed",
                    response.Error?.Message ?? "MicMixer rejected the command.");
            }

            if (response.Data is not JsonElement data)
            {
                throw new IOException("MicMixer returned a response without data.");
            }

            return data.Deserialize<T>(JsonOptions)
                ?? throw new IOException("MicMixer returned an invalid response payload.");
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task SendStateCommandAsync(
        string command,
        object? payload,
        CancellationToken cancellationToken)
    {
        MicMixerMusicState state = await SendRequestAsync<MicMixerMusicState>(
            command,
            payload,
            requireConnectedState: true,
            cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _state, state);
        RaiseOnEventContext(() => StateChanged?.Invoke(this, state));
    }

    public Task<MicMixerMusicState> GetStateAsync(CancellationToken cancellationToken = default) =>
        SendRequestAsync<MicMixerMusicState>("getState", null, true, cancellationToken);

    public Task<MicMixerTrackPage> GetTracksAsync(
        string? search = null,
        string? folderId = null,
        int offset = 0,
        int limit = 200,
        CancellationToken cancellationToken = default) =>
        SendRequestAsync<MicMixerTrackPage>(
            "getTracks",
            new { search, folderId, offset, limit },
            true,
            cancellationToken);

    public Task<IReadOnlyList<MicMixerMusicFolder>> GetFoldersAsync(
        CancellationToken cancellationToken = default) =>
        SendRequestAsync<IReadOnlyList<MicMixerMusicFolder>>("getFolders", null, true, cancellationToken);

    public Task RefreshLibraryAsync(CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("refreshLibrary", null, cancellationToken);

    public Task AddMusicFolderAsync(string path, CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("addMusicFolder", new { path }, cancellationToken);

    public Task RemoveMusicFolderAsync(string folderId, CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("removeMusicFolder", new { folderId }, cancellationToken);

    public Task ResetMusicFoldersAsync(CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("resetMusicFolders", null, cancellationToken);

    public Task SetDownloadFolderAsync(string folderId, CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("setDownloadFolder", new { folderId }, cancellationToken);

    public Task SwitchToLibraryModeAsync(CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("switchToLibraryMode", null, cancellationToken);

    public Task DownloadFromUrlAsync(
        string url,
        string? folderId = null,
        CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("downloadFromUrl", new { url, folderId }, cancellationToken);

    public Task PlayTrackAsync(string trackId, CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("playTrack", new { trackId }, cancellationToken);

    public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("togglePlayPause", null, cancellationToken);

    public Task StopPlaybackAsync(CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("stop", null, cancellationToken);

    public Task PreviousAsync(CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("previous", null, cancellationToken);

    public Task NextAsync(CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("next", null, cancellationToken);

    public Task SeekAsync(double positionSeconds, CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("seek", new { positionSeconds }, cancellationToken);

    public Task SetMusicVolumeAsync(double volume, CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("setMusicVolume", new { volume }, cancellationToken);

    public Task SetMonitorVolumeAsync(double volume, CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("setMonitorVolume", new { volume }, cancellationToken);

    public Task SetVolumesLinkedAsync(bool linked, CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("setVolumesLinked", new { linked }, cancellationToken);

    public Task EnqueueTrackAsync(string trackId, CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("enqueueTrack", new { trackId }, cancellationToken);

    public Task RemoveQueueItemAsync(int index, CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("removeQueueItem", new { index }, cancellationToken);

    public Task MoveQueueItemAsync(int fromIndex, int toIndex, CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("moveQueueItem", new { fromIndex, toIndex }, cancellationToken);

    public Task ClearQueueAsync(CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("clearQueue", null, cancellationToken);

    public Task StartDelayedPlayAsync(string? trackId = null, CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("startDelayedPlay", trackId == null ? null : new { trackId }, cancellationToken);

    public Task CancelDelayedPlayAsync(CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("cancelDelayedPlay", null, cancellationToken);

    public Task SetDelayedStartSecondsAsync(int seconds, CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("setDelayedStartSeconds", new { seconds }, cancellationToken);

    public Task SetSingleTrackModeAsync(
        MicMixerSingleTrackMode mode,
        CancellationToken cancellationToken = default) =>
        SendStateCommandAsync("setSingleTrackMode", new { mode = mode.ToString() }, cancellationToken);

    private void SetConnectionState(MicMixerConnectionState value)
    {
        MicMixerConnectionState previous = (MicMixerConnectionState)Interlocked.Exchange(ref _connectionState, (int)value);
        if (previous != value)
        {
            RaiseOnEventContext(() => ConnectionStateChanged?.Invoke(this, value));
        }
    }

    private void RaiseOnEventContext(Action action)
    {
        if (_eventContext == null || ReferenceEquals(SynchronizationContext.Current, _eventContext))
        {
            action();
            return;
        }

        _eventContext.Post(static state => ((Action)state!).Invoke(), action);
    }

    private void FailPending(Exception exception)
    {
        foreach ((string id, TaskCompletionSource<WireEnvelope> completion) in _pending)
        {
            if (_pending.TryRemove(id, out _))
            {
                completion.TrySetException(exception);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        finally
        {
            DisposeOwnedResources();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            DisposeOwnedResources();
        }
    }

    private void DisposeOwnedResources()
    {
        _lifetimeCancellation?.Dispose();
        _writeLock.Dispose();
    }

    private readonly record struct BoundedLine(string? Value, bool TooLarge, bool EndOfStream);

    private sealed class BoundedLineReader
    {
        private const int BufferSize = 4_096;

        private readonly StreamReader _reader;
        private readonly int _maximumCharacters;
        private readonly char[] _buffer = new char[BufferSize];
        private int _bufferOffset;
        private int _bufferCount;

        public BoundedLineReader(StreamReader reader, int maximumCharacters)
        {
            _reader = reader;
            _maximumCharacters = maximumCharacters;
        }

        public async ValueTask<BoundedLine> ReadAsync(CancellationToken cancellationToken)
        {
            var value = new System.Text.StringBuilder(Math.Min(_maximumCharacters, BufferSize));
            bool tooLarge = false;
            bool hasCharacters = false;

            while (true)
            {
                if (_bufferOffset >= _bufferCount)
                {
                    _bufferCount = await _reader.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                    _bufferOffset = 0;
                    if (_bufferCount == 0)
                    {
                        return hasCharacters
                            ? Complete(value, tooLarge)
                            : new BoundedLine(null, false, true);
                    }
                }

                int newline = Array.IndexOf(_buffer, '\n', _bufferOffset, _bufferCount - _bufferOffset);
                int segmentEnd = newline >= 0 ? newline : _bufferCount;
                int segmentLength = segmentEnd - _bufferOffset;
                hasCharacters |= segmentLength > 0;

                if (!tooLarge && value.Length + segmentLength <= _maximumCharacters)
                {
                    value.Append(_buffer, _bufferOffset, segmentLength);
                }
                else if (segmentLength > 0)
                {
                    tooLarge = true;
                    value.Clear();
                }

                _bufferOffset = newline >= 0 ? newline + 1 : segmentEnd;
                if (newline >= 0)
                {
                    return Complete(value, tooLarge);
                }
            }
        }

        private static BoundedLine Complete(System.Text.StringBuilder value, bool tooLarge)
        {
            if (tooLarge)
            {
                return new BoundedLine(null, true, false);
            }

            if (value.Length > 0 && value[^1] == '\r')
            {
                value.Length--;
            }

            return new BoundedLine(value.ToString(), false, false);
        }
    }

    private sealed record WireRequest(string Id, string Command, object? Payload, int ProtocolVersion);
    private sealed record WireError(string Code, string Message);
    private sealed record WireEnvelope(
        string Type,
        string? Id,
        bool? Success,
        JsonElement? Data,
        WireError? Error,
        int ProtocolVersion);
}
