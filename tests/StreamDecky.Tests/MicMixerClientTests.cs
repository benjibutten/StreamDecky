using System.IO.Pipes;
using System.Text.Json;
using StreamDecky.Integrations.MicMixer;
using Xunit;

namespace StreamDecky.Tests;

public sealed class MicMixerClientTests
{
    [Fact]
    public async Task Dispose_ShouldStopTheConnectionSynchronously()
    {
        string pipeName = $"StreamDecky.Tests.MicMixer.{Guid.NewGuid():N}";
        using var serverCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task serverTask = RunFakeServerAsync(pipeName, serverCancellation.Token);

        var client = new MicMixerClient(pipeName, eventContext: null);
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ConnectionStateChanged += (_, state) =>
        {
            if (state == MicMixerConnectionState.Connected)
                connected.TrySetResult();
        };

        client.Start();
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        client.Dispose();

        Assert.Equal(MicMixerConnectionState.Stopped, client.ConnectionState);
        Assert.False(client.IsConnected);
        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Client_ShouldHandshakeReceiveStateAndSendCommands()
    {
        string pipeName = $"StreamDecky.Tests.MicMixer.{Guid.NewGuid():N}";
        using var serverCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task serverTask = RunFakeServerAsync(pipeName, serverCancellation.Token);

        await using var client = new MicMixerClient(pipeName, eventContext: null);
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stateReceived = new TaskCompletionSource<MicMixerMusicState>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ConnectionStateChanged += (_, state) =>
        {
            if (state == MicMixerConnectionState.Connected)
                connected.TrySetResult();
        };
        client.StateChanged += (_, state) => stateReceived.TrySetResult(state);

        client.Start();
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        MicMixerMusicState eventState = await stateReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        MicMixerTrackPage tracks = await client.GetTracksAsync("song", limit: 25);
        IReadOnlyList<MicMixerMusicFolder> folders = await client.GetFoldersAsync();

        Assert.True(client.IsConnected);
        Assert.Equal("Playing", eventState.PlaybackState);
        Assert.Equal("Track one", eventState.CurrentTrackName);
        Assert.Single(tracks.Tracks);
        Assert.Equal("Song one", tracks.Tracks[0].Name);
        Assert.Equal(@"C:\Music", tracks.Tracks[0].FolderPath);
        Assert.Single(folders);
        Assert.Equal(@"C:\Music", folders[0].Path);

        await client.StopAsync();
        serverCancellation.Cancel();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    [Fact]
    public async Task Client_ShouldGiveUpAfterMaxFailures_AndRetryOnStart()
    {
        string pipeName = $"StreamDecky.Tests.MicMixer.Missing.{Guid.NewGuid():N}";
        await using var client = new MicMixerClient(pipeName, eventContext: null, maxConsecutiveConnectFailures: 1);

        var unavailable = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retriedAfterGivingUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ConnectionStateChanged += (_, state) =>
        {
            if (state == MicMixerConnectionState.Unavailable)
                unavailable.TrySetResult();
            else if (state == MicMixerConnectionState.Connecting && unavailable.Task.IsCompleted)
                retriedAfterGivingUp.TrySetResult();
        };

        client.Start();
        await unavailable.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(MicMixerConnectionState.Unavailable, client.ConnectionState);

        // A new Start() restarts the reconnect loop after it gave up.
        client.Start();
        await retriedAfterGivingUp.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Client_ShouldRejectOversizedServerMessage()
    {
        string pipeName = $"StreamDecky.Tests.MicMixer.Oversized.{Guid.NewGuid():N}";
        using var serverCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task serverTask = RunOversizedMessageServerAsync(pipeName, serverCancellation.Token);

        await using var client = new MicMixerClient(
            pipeName,
            eventContext: null,
            maxConsecutiveConnectFailures: 1);
        var unavailable = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ConnectionStateChanged += (_, state) =>
        {
            if (state == MicMixerConnectionState.Unavailable)
                unavailable.TrySetResult();
        };

        client.Start();

        await unavailable.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(MicMixerConnectionState.Unavailable, client.ConnectionState);
        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task RunFakeServerAsync(string pipeName, CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.WaitForConnectionAsync(cancellationToken);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

        try
        {
            await ServeAsync(pipe, reader, writer, cancellationToken);
        }
        finally
        {
            DisposeIgnoringBrokenPipe(writer);
        }
    }

    private static async Task ServeAsync(
        NamedPipeServerStream pipe,
        StreamReader reader,
        StreamWriter writer,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && pipe.IsConnected)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line == null)
                return;

            using JsonDocument request = JsonDocument.Parse(line);
            string id = request.RootElement.GetProperty("id").GetString()!;
            string command = request.RootElement.GetProperty("command").GetString()!;

            if (command == "hello")
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    type = "response",
                    id,
                    success = true,
                    data = new
                    {
                        product = "MicMixer",
                        version = "1.0.0",
                        protocolVersion = 1,
                        capabilities = new[] { "state", "library" }
                    },
                    protocolVersion = 1
                }));

                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    type = "event",
                    data = new { name = "stateChanged", state = CreateState() },
                    protocolVersion = 1
                }));
                continue;
            }

            if (command == "getTracks")
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    type = "response",
                    id,
                    success = true,
                    data = new
                    {
                        offset = 0,
                        limit = 25,
                        total = 1,
                        libraryVersion = "library",
                        tracks = new[]
                        {
                            new
                            {
                                id = "track-1",
                                name = "Song one",
                                folderId = "folder-1",
                                folderName = "Default",
                                folderPath = @"C:\Music",
                                isPlaying = true,
                                queuePositions = Array.Empty<int>()
                            }
                        }
                    },
                    protocolVersion = 1
                }));
                continue;
            }

            if (command == "getFolders")
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    type = "response",
                    id,
                    success = true,
                    data = new[]
                    {
                        new
                        {
                            id = "folder-1",
                            name = "Default",
                            path = @"C:\Music",
                            isDefault = true,
                            isPreferredDownloadFolder = true,
                            isEffectiveDownloadFolder = true
                        }
                    },
                    protocolVersion = 1
                }));
            }
        }
    }

    private static async Task RunOversizedMessageServerAsync(
        string pipeName,
        CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.WaitForConnectionAsync(cancellationToken);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

        try
        {
            _ = await reader.ReadLineAsync(cancellationToken)
                ?? throw new IOException("Client disconnected before sending hello.");
            await writer.WriteLineAsync(new string('x', 1_048_577));
        }
        finally
        {
            DisposeIgnoringBrokenPipe(writer);
        }
    }

    // The client tears the pipe down first, so the final flush in StreamWriter.Dispose
    // can race with the OS marking the pipe broken.
    private static void DisposeIgnoringBrokenPipe(StreamWriter writer)
    {
        try
        {
            writer.Dispose();
        }
        catch (IOException)
        {
        }
    }

    private static object CreateState() => new
    {
        playbackState = "Playing",
        isExternalMode = false,
        currentTrackId = "track-1",
        currentTrackName = "Track one",
        positionSeconds = 12d,
        durationSeconds = 60d,
        musicVolume = 0.5d,
        monitorVolume = 0.5d,
        isRouting = true,
        hasMonitorOutput = false,
        canStartPlayback = true,
        playbackBlockedReason = (string?)null,
        isDelayedStartActive = false,
        delayedStartSeconds = 3,
        delayedStartRemainingSeconds = 0,
        singleTrackMode = "Off",
        libraryVersion = "library",
        queueVersion = "queue",
        folders = new[]
        {
            new
            {
                id = "folder-1",
                name = "Default",
                path = @"C:\Music",
                isDefault = true,
                isPreferredDownloadFolder = true,
                isEffectiveDownloadFolder = true
            }
        },
        queue = Array.Empty<object>(),
        isDownloading = false,
        downloadPercent = (double?)null,
        downloadStatus = "",
        statusText = "Playing"
    };
}
