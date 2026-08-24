using System.Collections.Concurrent;

namespace EmaBot.Api.Mt5Bridge;

public sealed class Mt5BridgeSession : IAsyncDisposable
{
    private readonly Stream _pipe;
    private readonly Mt5BridgeFrameCodec _codec;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _requestTimeout;
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<Mt5BridgeEnvelope>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _stopped = new();

    public Mt5BridgeSession(Stream pipe, Mt5BridgeFrameCodec codec, TimeProvider timeProvider, TimeSpan requestTimeout)
    {
        _pipe = pipe;
        _codec = codec;
        _timeProvider = timeProvider;
        _requestTimeout = requestTimeout;
    }

    public int PendingRequestCount => _pending.Count;

    public async Task RunAsync(Action<Mt5BridgeEnvelope> onMessage, Action<DateTimeOffset> onHeartbeat, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopped.Token);
        try
        {
            while (!linked.IsCancellationRequested)
            {
                var envelope = await _codec.ReadAsync(_pipe, linked.Token) ?? throw new Mt5BridgeDisconnectedException("MT5 bridge client disconnected.");
                if (envelope.Kind == Mt5BridgeFrameKind.Hello) throw new Mt5BridgeProtocolException("A second Hello frame is not allowed.");
                if (envelope.ProtocolVersion != Mt5BridgeProtocol.ProtocolVersion) throw new Mt5BridgeProtocolException("Unsupported bridge protocol version.");
                if (envelope.Kind == Mt5BridgeFrameKind.Heartbeat)
                {
                    if (envelope.Operation != Mt5BridgeOperation.Heartbeat) throw new Mt5BridgeProtocolException("Heartbeat operation is invalid.");
                    onMessage(envelope);
                    onHeartbeat(_timeProvider.GetUtcNow());
                    continue;
                }
                if (envelope.Kind is Mt5BridgeFrameKind.Response or Mt5BridgeFrameKind.Error)
                {
                    onMessage(envelope);
                    if (envelope.RequestId is { } requestId && _pending.TryRemove(requestId, out var pending))
                    {
                        if (envelope.Kind == Mt5BridgeFrameKind.Response) pending.TrySetResult(envelope);
                        else
                        {
                            var error = envelope.DeserializePayload<Mt5BridgeErrorPayload>() ?? new("InternalError", "The MT5 terminal returned an invalid error.", false);
                            pending.TrySetException(new Mt5BridgeRemoteException(error.Code, error.Message, error.Retryable));
                        }
                    }
                    continue;
                }
                throw new Mt5BridgeProtocolException("Unexpected bridge frame kind.");
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        finally { FailPending(new Mt5BridgeDisconnectedException("MT5 bridge client disconnected.")); }
    }

    public async Task<Mt5BridgeEnvelope> SendRequestAsync(Mt5BridgeOperation operation, object? payload, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var pending = new TaskCompletionSource<Mt5BridgeEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, pending)) throw new Mt5BridgeProtocolException("Could not register bridge request.");
        using var deadline = new CancellationTokenSource(_requestTimeout);
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopped.Token, deadline.Token);
        var frameWriteStarted = false;
        var frameWriteCompleted = false;
        try
        {
            await WriteRequestAsync(Mt5BridgeEnvelope.Create(Mt5BridgeFrameKind.Request, operation, requestId, payload, _timeProvider), requestCancellation.Token, () => frameWriteStarted = true);
            frameWriteCompleted = true;
            return await pending.Task.WaitAsync(requestCancellation.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { PoisonPartialFrame(frameWriteStarted, frameWriteCompleted); throw; }
        catch (OperationCanceledException) when (_stopped.IsCancellationRequested) { PoisonPartialFrame(frameWriteStarted, frameWriteCompleted); throw new Mt5BridgeDisconnectedException("MT5 bridge client disconnected."); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested) { PoisonPartialFrame(frameWriteStarted, frameWriteCompleted); throw new Mt5BridgeRequestTimeoutException("MT5 bridge request timed out."); }
        catch { PoisonPartialFrame(frameWriteStarted, frameWriteCompleted); throw; }
        finally { _pending.TryRemove(requestId, out _); }
    }

    public async Task WriteAsync(Mt5BridgeEnvelope envelope, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try { await _codec.WriteAsync(_pipe, envelope, cancellationToken); }
        finally { _writeLock.Release(); }
    }

    private async Task WriteRequestAsync(Mt5BridgeEnvelope envelope, CancellationToken cancellationToken, Action onFrameWriteStarted)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            onFrameWriteStarted();
            await _codec.WriteAsync(_pipe, envelope, cancellationToken);
        }
        finally { _writeLock.Release(); }
    }

    private void PoisonPartialFrame(bool frameWriteStarted, bool frameWriteCompleted)
    {
        if (frameWriteStarted && !frameWriteCompleted) Disconnect();
    }

    public void Disconnect() => _stopped.Cancel();

    public void FailPending(Exception exception)
    {
        foreach (var pair in _pending)
            if (_pending.TryRemove(pair.Key, out var pending)) pending.TrySetException(exception);
    }

    public async ValueTask DisposeAsync()
    {
        Disconnect();
        FailPending(new Mt5BridgeDisconnectedException("MT5 bridge session stopped."));
        _pipe.Dispose();
        _writeLock.Dispose();
        _stopped.Dispose();
        await ValueTask.CompletedTask;
    }
}
