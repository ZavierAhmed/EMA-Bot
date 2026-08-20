using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Mt5Bridge;

public sealed record Mt5ExecutionBridgeStatus(bool Enabled, bool Connected, string PipeName, string? AccountFingerprint, string? AccountServer, string? AccountMode, string? LastDisconnectReason);
public interface IMt5ExecutionBridgeClient
{
    bool IsConnected { get; }
    event Action? Connected;
    Mt5ExecutionBridgeStatus GetStatus();
    Task<Mt5ExecutionEnvelope> SendAsync(Mt5ExecutionOperation operation, object? payload, CancellationToken token);
}

// A dedicated v2 pipe.  It has no dependency on the v1 market-data bridge.
public sealed class Mt5ExecutionBridgeServer : IHostedService, IMt5ExecutionBridgeClient, IAsyncDisposable
{
    private readonly Mt5ExecutionBridgeOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<Mt5ExecutionBridgeServer> _logger;
    private readonly object _sync = new();
    private Mt5ExecutionBridgeStatus _status;
    private ExecutionConnection? _connection;
    private CancellationTokenSource? _stopping;
    private Task? _loop;

    public Mt5ExecutionBridgeServer(IOptions<Mt5ExecutionBridgeOptions> options, TimeProvider clock, ILogger<Mt5ExecutionBridgeServer> logger)
    {
        _options = options.Value; _clock = clock; _logger = logger;
        _status = new Mt5ExecutionBridgeStatus(_options.Enabled, false, _options.PipeName, null, null, null, null);
    }

    public bool IsConnected { get { lock (_sync) return _connection is not null; } }
    public event Action? Connected;
    public Mt5ExecutionBridgeStatus GetStatus() { lock (_sync) return _status; }

    public Task StartAsync(CancellationToken token)
    {
        if (!_options.Enabled) return Task.CompletedTask;
        var errors = Mt5ExecutionBridgeOptions.Validate(_options);
        if (errors.Count != 0) throw new OptionsValidationException(Mt5ExecutionBridgeOptions.SectionName, typeof(Mt5ExecutionBridgeOptions), errors);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The enabled MT5 execution bridge requires Windows local pipe security.");
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(token);
        _loop = Task.Run(() => AcceptLoopAsync(_stopping.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken token)
    {
        _stopping?.Cancel();
        ExecutionConnection? connection; Task? loop;
        lock (_sync) { connection = _connection; _connection = null; loop = _loop; }
        if (connection is not null) await connection.DisposeAsync();
        if (loop is not null) try { await loop.WaitAsync(token); } catch (OperationCanceledException) { }
        _stopping?.Dispose(); _stopping = null; _loop = null;
    }

    public async Task<Mt5ExecutionEnvelope> SendAsync(Mt5ExecutionOperation operation, object? payload, CancellationToken token)
    {
        if (!Mt5ExecutionBridgeProtocol.AllowedWriteOperations.Contains(operation) && operation != Mt5ExecutionOperation.GetExecutionAccount) throw new ArgumentOutOfRangeException(nameof(operation));
        ExecutionConnection? connection; lock (_sync) connection = _connection;
        if (connection is null) throw new Mt5ExecutionBridgeUnavailableException("The MT5 execution bridge v2 is not connected.");
        return await connection.RequestAsync(operation, payload, token);
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            ExecutionConnection? connection = null;
            var disconnectReason = "MT5 execution bridge client disconnected.";
            var retryAfterDisconnect = false;
            try
            {
                pipe = new NamedPipeServerStream(_options.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token);
                connection = new ExecutionConnection(pipe, _options, _clock);
                pipe = null;
                var hello = await connection.ReceiveHelloAsync(token);
                if (!SecretsMatch(_options.HandshakeSecret!, hello.Secret))
                {
                    disconnectReason = "MT5 execution bridge handshake was rejected.";
                    retryAfterDisconnect = true;
                }
                else
                {
                    lock (_sync) { _connection = connection; _status = new(true, true, _options.PipeName, hello.AccountFingerprint, hello.AccountServer, hello.AccountMode, null); }
                    await connection.WriteAsync(Mt5ExecutionEnvelope.Create(Mt5ExecutionFrameKind.HelloAck, Mt5ExecutionOperation.Hello, null, new { protocolVersion = 2, serverVersion = "EMA-Bot" }, _clock), token);
                    Connected?.Invoke();
                    await connection.PumpAsync(token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "MT5 execution bridge v2 connection ended.");
                disconnectReason = "MT5 execution bridge connection ended.";
                retryAfterDisconnect = true;
            }
            finally
            {
                if (connection is not null) await connection.DisposeAsync();
                else pipe?.Dispose();

                lock (_sync)
                {
                    if (ReferenceEquals(_connection, connection)) _connection = null;
                    _status = _status with { Connected = false, LastDisconnectReason = disconnectReason };
                }
            }

            if (retryAfterDisconnect)
            {
                try { await Task.Delay(TimeSpan.FromMilliseconds(250), token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            }
        }
    }
    private static bool SecretsMatch(string expected, string actual) => CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(actual)));
    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));

    private sealed class ExecutionConnection : IAsyncDisposable
    {
        private readonly NamedPipeServerStream _pipe; private readonly Mt5ExecutionBridgeOptions _options; private readonly TimeProvider _clock;
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<Mt5ExecutionEnvelope>> _pending = new(); private readonly SemaphoreSlim _writes = new(1, 1);
        private int _disposed;
        public ExecutionConnection(NamedPipeServerStream pipe, Mt5ExecutionBridgeOptions options, TimeProvider clock) { _pipe = pipe; _options = options; _clock = clock; }
        public async Task<Mt5ExecutionHelloPayload> ReceiveHelloAsync(CancellationToken token)
        {
            var envelope = await ReadAsync(token);
            var hello = envelope?.DeserializePayload<Mt5ExecutionHelloPayload>();
            if (envelope is null || envelope.ProtocolVersion != 2 || envelope.Kind != Mt5ExecutionFrameKind.Hello || envelope.Operation != Mt5ExecutionOperation.Hello || hello is null || string.IsNullOrWhiteSpace(hello.Secret)) throw new Mt5ExecutionBridgeException("Execution bridge v2 handshake was invalid.");
            return hello;
        }
        public async Task PumpAsync(CancellationToken token)
        {
            try { while (!token.IsCancellationRequested) { var envelope = await ReadAsync(token) ?? throw new EndOfStreamException(); if (envelope.ProtocolVersion != 2) throw new Mt5ExecutionBridgeException("Unsupported execution bridge protocol version."); if (envelope.Kind is Mt5ExecutionFrameKind.Response or Mt5ExecutionFrameKind.Error && envelope.RequestId is { } id && _pending.TryRemove(id, out var waiter)) { if (envelope.Kind == Mt5ExecutionFrameKind.Response) waiter.TrySetResult(envelope); else waiter.TrySetException(new Mt5ExecutionBridgeException(envelope.DeserializePayload<Mt5ExecutionErrorPayload>()?.Message ?? "MT5 execution bridge rejected the request.")); } } }
            finally { foreach (var pair in _pending) if (_pending.TryRemove(pair.Key, out var pending)) pending.TrySetException(new Mt5ExecutionBridgeAmbiguousException("MT5 execution bridge disconnected after a request.")); }
        }
        public async Task<Mt5ExecutionEnvelope> RequestAsync(Mt5ExecutionOperation operation, object? payload, CancellationToken token)
        {
            var id = Guid.NewGuid(); var waiter = new TaskCompletionSource<Mt5ExecutionEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously); if (!_pending.TryAdd(id, waiter)) throw new Mt5ExecutionBridgeException("Unable to register bridge request.");
            try { await WriteAsync(Mt5ExecutionEnvelope.Create(Mt5ExecutionFrameKind.Request, operation, id, payload, _clock), token); return await waiter.Task.WaitAsync(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds), token); }
            catch (TimeoutException) { throw new Mt5ExecutionBridgeAmbiguousException("MT5 execution bridge request timed out."); }
            finally { _pending.TryRemove(id, out _); }
        }
        public async Task WriteAsync(Mt5ExecutionEnvelope envelope, CancellationToken token)
        { var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, Mt5ExecutionBridgeProtocol.JsonOptions); if (bytes.Length == 0 || bytes.Length > _options.MaxFrameBytes) throw new Mt5ExecutionBridgeException("Execution bridge frame is invalid."); await _writes.WaitAsync(token); try { await _pipe.WriteAsync(BitConverter.GetBytes(bytes.Length), token); await _pipe.WriteAsync(bytes, token); await _pipe.FlushAsync(token); } finally { _writes.Release(); } }
        private async Task<Mt5ExecutionEnvelope?> ReadAsync(CancellationToken token)
        { var header = new byte[4]; if (!await ReadExactlyAsync(header, true, token)) return null; var length = BitConverter.ToInt32(header); if (length <= 0 || length > _options.MaxFrameBytes) throw new Mt5ExecutionBridgeException("Execution bridge frame length is invalid."); var body = new byte[length]; await ReadExactlyAsync(body, false, token); return JsonSerializer.Deserialize<Mt5ExecutionEnvelope>(body, Mt5ExecutionBridgeProtocol.JsonOptions) ?? throw new Mt5ExecutionBridgeException("Execution bridge JSON is invalid."); }
        private async Task<bool> ReadExactlyAsync(byte[] buffer, bool cleanEnd, CancellationToken token) { var offset = 0; while (offset < buffer.Length) { var read = await _pipe.ReadAsync(buffer.AsMemory(offset), token); if (read == 0) { if (offset == 0 && cleanEnd) return false; throw new EndOfStreamException(); } offset += read; } return true; }
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _pipe.Dispose();
                _writes.Dispose();
            }
            return ValueTask.CompletedTask;
        }
    }
}
