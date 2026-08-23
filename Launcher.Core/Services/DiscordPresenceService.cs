using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Launcher.Core.Services;

public sealed class DiscordPresenceService : IDiscordPresenceService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _applicationId;
    private Stream? _stream;
    private CancellationTokenSource? _readerLifetime;
    private DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private bool _isReady;

    public bool IsEnabled { get; set; } = true;

    public DiscordPresenceService(string? applicationId)
    {
        _applicationId = NormalizeApplicationId(applicationId);
    }

    public void SetApplicationId(string? applicationId)
    {
        var normalized = NormalizeApplicationId(applicationId);
        if (string.Equals(_applicationId, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _applicationId = normalized;
        Disconnect();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(_applicationId))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isReady && _stream is not null)
            {
                return;
            }

            await ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetLauncherIdleAsync(
        string accountName,
        string profileName,
        CancellationToken cancellationToken = default)
    {
        await SetPresenceAsync(
            Environment.ProcessId,
            new PresenceActivity(
                "In the launcher",
                $"{profileName} · {accountName}",
                0),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SetPlayingAsync(
        string accountName,
        string profileName,
        string minecraftVersion,
        int processId,
        CancellationToken cancellationToken = default)
    {
        await SetPresenceAsync(
            processId,
            new PresenceActivity(
                $"Playing Minecraft {minecraftVersion}",
                $"{profileName} · {accountName}",
                0),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (_stream is null || !_isReady)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SendSetActivityAsync(Environment.ProcessId, null, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Disconnect();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        Disconnect();
        _gate.Dispose();
    }

    private async Task SetPresenceAsync(
        int processId,
        PresenceActivity activity,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            await ClearAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(_applicationId))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_isReady || _stream is null)
            {
                await ConnectAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_stream is null || !_isReady)
            {
                return;
            }

            await SendSetActivityAsync(processId, activity, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Disconnect();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        Disconnect();
        _startedAt = DateTimeOffset.UtcNow;
        _stream = await OpenIpcStreamAsync(cancellationToken).ConfigureAwait(false);
        await WriteFrameAsync(0, new { v = 1, client_id = _applicationId }, cancellationToken).ConfigureAwait(false);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            var frame = await ReadFrameAsync(_stream, cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (frame.Value.OpCode == 3)
            {
                await WriteFrameRawAsync(4, frame.Value.Body, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (frame.Value.OpCode != 1)
            {
                continue;
            }

            var payload = Encoding.UTF8.GetString(frame.Value.Body);
            if (payload.Contains("\"cmd\":\"DISPATCH\"", StringComparison.Ordinal)
                && payload.Contains("\"evt\":\"READY\"", StringComparison.Ordinal))
            {
                _isReady = true;
                StartReaderLoop();
                return;
            }

            if (payload.Contains("\"cmd\":\"DISPATCH\"", StringComparison.Ordinal)
                && payload.Contains("\"evt\":\"ERROR\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Discord IPC error: {payload}");
            }
        }

        throw new TimeoutException("Discord did not respond with READY.");
    }

    private void StartReaderLoop()
    {
        _readerLifetime?.Cancel();
        _readerLifetime?.Dispose();
        _readerLifetime = new CancellationTokenSource();
        var token = _readerLifetime.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && _stream is not null)
            {
                try
                {
                    var frame = await ReadFrameAsync(_stream, token).ConfigureAwait(false);
                    if (frame is null)
                    {
                        await Task.Delay(100, token).ConfigureAwait(false);
                        continue;
                    }

                    if (frame.Value.OpCode == 3)
                    {
                        await WriteFrameRawAsync(4, frame.Value.Body, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    Disconnect();
                    break;
                }
            }
        }, token);
    }

    private async Task SendSetActivityAsync(
        int processId,
        PresenceActivity? activity,
        CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            return;
        }

        await WriteFrameAsync(1, new
        {
            cmd = "SET_ACTIVITY",
            args = new
            {
                pid = processId,
                activity = activity?.ToPayload(_startedAt)
            },
            nonce = Guid.NewGuid().ToString("N")
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteFrameAsync(int opCode, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonSerializerOptions);
        await WriteFrameRawAsync(opCode, Encoding.UTF8.GetBytes(json), cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteFrameRawAsync(int opCode, byte[] body, CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            return;
        }

        var header = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), opCode);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), body.Length);
        await _stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (body.Length > 0)
        {
            await _stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        }

        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(int OpCode, byte[] Body)?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[8];
        var read = await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (!read)
        {
            return null;
        }

        var opCode = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
        if (length < 0 || length > 1_048_576)
        {
            throw new InvalidDataException($"Discord IPC frame length {length} is invalid.");
        }

        var body = length == 0 ? [] : new byte[length];
        if (length > 0 && !await ReadExactAsync(stream, body, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return (opCode, body);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static async Task<Stream> OpenIpcStreamAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Discord IPC is only supported on Windows in DanClient.");
        }

        for (var i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(
                ".",
                $"discord-ipc-{i}",
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(1000, cancellationToken).ConfigureAwait(false);
                return pipe;
            }
            catch
            {
                pipe.Dispose();
            }
        }

        throw new InvalidOperationException("Discord is not running or Rich Presence is unavailable.");
    }

    private void Disconnect()
    {
        _readerLifetime?.Cancel();
        _readerLifetime?.Dispose();
        _readerLifetime = null;
        _stream?.Dispose();
        _stream = null;
        _isReady = false;
    }

    private static string? NormalizeApplicationId(string? applicationId) =>
        string.IsNullOrWhiteSpace(applicationId) ? null : applicationId.Trim();

    private static JsonSerializerOptions JsonSerializerOptions { get; } = new(JsonSerializerDefaults.Web);

    private sealed record PresenceActivity(string Details, string State, int Type)
    {
        public object? ToPayload(DateTimeOffset startedAt) => new
        {
            type = Type,
            details = Details,
            state = State,
            timestamps = new
            {
                start = startedAt.ToUnixTimeSeconds()
            }
        };
    }
}
