using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace EmaBot.Api.Mt5Bridge;

public sealed class Mt5BridgeServer : IHostedService, IMt5BridgeRequestClient, IAsyncDisposable
{
    private readonly Mt5BridgeOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<Mt5BridgeServer> _logger;
    private readonly object _sync = new();
    private readonly Mt5BridgeFrameCodec _codec;
    private readonly Mt5BridgeStatus _initialStatus;
    private CancellationTokenSource? _stopping;
    private Task? _acceptLoop;
    private Mt5BridgeSession? _session;
    private Mt5BridgeStatus _status;

    public Mt5BridgeServer(IOptions<Mt5BridgeOptions> options, TimeProvider timeProvider, ILogger<Mt5BridgeServer> logger)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _codec = new Mt5BridgeFrameCodec(_options.MaxFrameBytes);
        _initialStatus = NewStatus(_options.Enabled ? Mt5BridgeConnectionState.WaitingForClient : Mt5BridgeConnectionState.Disabled);
        _status = _initialStatus;
    }

    public bool IsConnected => GetStatus().ConnectionState == Mt5BridgeConnectionState.Connected;
    public int PendingRequestCount { get { lock (_sync) return _session?.PendingRequestCount ?? 0; } }
    public Mt5BridgeStatus GetStatus() { lock (_sync) return _status; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled) return Task.CompletedTask;
        var errors = Mt5BridgeOptions.Validate(_options);
        if (errors.Count != 0) throw new OptionsValidationException(Mt5BridgeOptions.SectionName, typeof(Mt5BridgeOptions), errors);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The enabled MT5 bridge requires Windows local pipe security.");
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        SetStatus(NewStatus(Mt5BridgeConnectionState.WaitingForClient));
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping?.Cancel();
        Mt5BridgeSession? session;
        lock (_sync) session = _session;
        session?.Disconnect();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
    }

    public async Task<Mt5BridgeEnvelope> SendAsync(Mt5BridgeOperation operation, object? payload, CancellationToken cancellationToken)
    {
        if (!Mt5BridgeProtocol.AllowedRequestOperations.Contains(operation)) throw new ArgumentOutOfRangeException(nameof(operation), "Only approved read-only MT5 bridge operations may be sent.");
        Mt5BridgeSession? session;
        lock (_sync) session = _session;
        if (session is null || !IsConnected) throw new Mt5BridgeUnavailableException("The MT5 bridge is not connected.");
        var stopwatch = operation == Mt5BridgeOperation.Ping ? Stopwatch.StartNew() : null;
        var response = await session.SendRequestAsync(operation, payload, cancellationToken);
        if (stopwatch is not null)
        {
            stopwatch.Stop();
            lock (_sync) _status = _status with { LastRoundTripMs = stopwatch.ElapsedMilliseconds };
        }
        return response;
    }

    public bool IsHeartbeatStale(DateTimeOffset now)
    {
        var status = GetStatus();
        if (status.ConnectionState != Mt5BridgeConnectionState.Connected || status.ConnectedAtUtc is not { } connectedAt) return false;
        return now - (status.LastHeartbeatAtUtc ?? connectedAt) > TimeSpan.FromSeconds(_options.HeartbeatTimeoutSeconds);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var pipe = CreatePipe();
            try
            {
                var previous = GetStatus();
                SetStatus(NewStatus(Mt5BridgeConnectionState.WaitingForClient) with { LastDisconnectAtUtc = previous.LastDisconnectAtUtc, LastDisconnectReason = previous.LastDisconnectReason });
                await pipe.WaitForConnectionAsync(cancellationToken);
                SetStatus(GetStatus() with { ConnectionState = Mt5BridgeConnectionState.Handshaking });
                var hello = await ReceiveHelloAsync(pipe, cancellationToken);
                if (hello is null) continue;
                var session = new Mt5BridgeSession(pipe, _codec, _timeProvider, TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
                lock (_sync) _session = session;
                var connectedAt = _timeProvider.GetUtcNow();
                var sessionId = Guid.NewGuid();
                SetStatus(new Mt5BridgeStatus(true, Mt5BridgeProtocol.ProtocolVersion, _options.PipeName, Mt5BridgeConnectionState.Connected, sessionId, connectedAt, connectedAt, null, GetStatus().LastDisconnectAtUtc, GetStatus().LastDisconnectReason, hello.ClientVersion, hello.TerminalInstanceId, hello.TerminalName, hello.TerminalCompany, hello.TerminalBuild, hello.AccountServer, hello.AccountCurrency, hello.AccountMode, null));
                await session.WriteAsync(Mt5BridgeEnvelope.Create(Mt5BridgeFrameKind.HelloAck, Mt5BridgeOperation.Hello, null, new Mt5HelloAckPayload(Mt5BridgeProtocol.ProtocolVersion, sessionId, "EMA-Bot", connectedAt, _options.HeartbeatTimeoutSeconds), _timeProvider), cancellationToken);
                using var monitor = new CancellationTokenSource();
                var monitorTask = MonitorHeartbeatAsync(session, monitor.Token);
                try
                {
                    await session.RunAsync(message => SetStatus(GetStatus() with { LastMessageAtUtc = _timeProvider.GetUtcNow() }), heartbeat => SetStatus(GetStatus() with { LastHeartbeatAtUtc = heartbeat }), cancellationToken);
                }
                finally
                {
                    monitor.Cancel();
                    try { await monitorTask; } catch (OperationCanceledException) { }
                    await session.DisposeAsync();
                    lock (_sync) _session = null;
                    SetDisconnected(GetStatus().ConnectionState == Mt5BridgeConnectionState.Stale ? "Heartbeat timed out." : "MT5 bridge client disconnected.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "MT5 bridge connection ended.");
                SetDisconnected("MT5 bridge connection ended.");
            }
        }
    }

    private NamedPipeServerStream CreatePipe() => new(_options.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task<Mt5HelloPayload?> ReceiveHelloAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.HandshakeTimeoutSeconds));
        try
        {
            var envelope = await _codec.ReadAsync(pipe, timeout.Token);
            var hello = envelope?.DeserializePayload<Mt5HelloPayload>();
            if (envelope is null || envelope.Kind != Mt5BridgeFrameKind.Hello || envelope.Operation != Mt5BridgeOperation.Hello || envelope.ProtocolVersion != Mt5BridgeProtocol.ProtocolVersion || hello is null || string.IsNullOrWhiteSpace(hello.Secret) || string.IsNullOrWhiteSpace(hello.ClientVersion) || string.IsNullOrWhiteSpace(hello.TerminalInstanceId) || !SecretsMatch(_options.HandshakeSecret!, hello.Secret))
            {
                await RejectHandshakeAsync(pipe, envelope?.RequestId, cancellationToken);
                return null;
            }
            return hello with { Secret = string.Empty };
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            SetDisconnected("MT5 bridge handshake timed out.");
            return null;
        }
        catch (Exception)
        {
            await RejectHandshakeAsync(pipe, null, cancellationToken);
            return null;
        }
    }

    private async Task RejectHandshakeAsync(NamedPipeServerStream pipe, Guid? requestId, CancellationToken cancellationToken)
    {
        try { await _codec.WriteAsync(pipe, Mt5BridgeEnvelope.Create(Mt5BridgeFrameKind.Error, Mt5BridgeOperation.Hello, requestId, new Mt5BridgeErrorPayload("AuthenticationFailed", "Bridge authentication failed.", false), _timeProvider), cancellationToken); }
        catch { }
        SetDisconnected("MT5 bridge handshake was rejected.");
    }

    private async Task MonitorHeartbeatAsync(Mt5BridgeSession session, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), _timeProvider, cancellationToken);
            if (IsHeartbeatStale(_timeProvider.GetUtcNow()))
            {
                SetStatus(GetStatus() with { ConnectionState = Mt5BridgeConnectionState.Stale });
                session.Disconnect();
                return;
            }
        }
    }

    private Mt5BridgeStatus NewStatus(Mt5BridgeConnectionState state) => new(_options.Enabled, Mt5BridgeProtocol.ProtocolVersion, _options.PipeName, state, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
    private void SetStatus(Mt5BridgeStatus status) { lock (_sync) _status = status; }
    private void SetDisconnected(string reason)
    {
        var current = GetStatus();
        SetStatus(current with { ConnectionState = Mt5BridgeConnectionState.WaitingForClient, SessionId = null, LastDisconnectAtUtc = _timeProvider.GetUtcNow(), LastDisconnectReason = reason });
    }

    private static bool SecretsMatch(string expected, string received)
    {
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var receivedHash = SHA256.HashData(Encoding.UTF8.GetBytes(received));
        return CryptographicOperations.FixedTimeEquals(expectedHash, receivedHash);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _stopping?.Dispose();
    }
}
