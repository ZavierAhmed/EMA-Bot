using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Auth;
using EmaBot.Api.Controllers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EmaBot.Api.Tests;

public sealed class Mt5BridgeFrameCodecTests
{
    [Fact]
    public async Task FrameCodec_RoundTripsEveryEnvelopeField()
    {
        var codec = new Mt5BridgeFrameCodec(1_024);
        var sentAt = new DateTimeOffset(2026, 8, 13, 10, 0, 0, TimeSpan.Zero);
        var requestId = Guid.NewGuid();
        var envelope = new Mt5BridgeEnvelope(Mt5BridgeProtocol.ProtocolVersion, Mt5BridgeFrameKind.Request, Mt5BridgeOperation.GetQuote, requestId, sentAt, JsonSerializer.SerializeToElement(new Mt5GetInstrumentRequest("terminal.symbol"), Mt5BridgeProtocol.JsonOptions));
        await using var stream = new MemoryStream();

        await codec.WriteAsync(stream, envelope, CancellationToken.None);
        stream.Position = 0;
        var result = await codec.ReadAsync(stream, CancellationToken.None);

        Assert.Equal(envelope.ProtocolVersion, result!.ProtocolVersion); Assert.Equal(envelope.Kind, result.Kind); Assert.Equal(envelope.Operation, result.Operation); Assert.Equal(envelope.RequestId, result.RequestId); Assert.Equal(envelope.SentAtUtc, result.SentAtUtc);
        Assert.Equal("terminal.symbol", result.DeserializePayload<Mt5GetInstrumentRequest>()!.BrokerSymbol);
    }

    [Fact]
    public async Task FrameCodec_ReconstructsPartialHeaderAndPayloadReads()
    {
        var codec = new Mt5BridgeFrameCodec(1_024);
        await using var source = new MemoryStream();
        await codec.WriteAsync(source, Mt5BridgeEnvelope.Create(Mt5BridgeFrameKind.Heartbeat, Mt5BridgeOperation.Heartbeat, null, new Mt5HeartbeatPayload(DateTimeOffset.UnixEpoch), TimeProvider.System), CancellationToken.None);
        await using var stream = new ChunkedReadStream(source.ToArray(), 2);

        var result = await codec.ReadAsync(stream, CancellationToken.None);

        Assert.Equal(Mt5BridgeFrameKind.Heartbeat, result!.Kind); Assert.Equal(Mt5BridgeOperation.Heartbeat, result.Operation);
    }

    [Fact]
    public async Task FrameCodec_RejectsZeroOversizedMalformedJsonAndInvalidUtf8()
    {
        var codec = new Mt5BridgeFrameCodec(16);
        await Assert.ThrowsAsync<Mt5BridgeProtocolException>(() => codec.ReadAsync(new MemoryStream([0, 0, 0, 0]), CancellationToken.None));
        var oversized = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(oversized, 17);
        await Assert.ThrowsAsync<Mt5BridgeProtocolException>(() => codec.ReadAsync(new MemoryStream(oversized), CancellationToken.None));
        await Assert.ThrowsAsync<Mt5BridgeProtocolException>(() => codec.ReadAsync(Frame(Encoding.UTF8.GetBytes("{")), CancellationToken.None));
        await Assert.ThrowsAsync<Mt5BridgeProtocolException>(() => codec.ReadAsync(Frame([0xC3, 0x28]), CancellationToken.None));
    }

    [Fact]
    public void Mql5IsoHelloWireJson_DeserializesWithExactUtcTimestamp()
    {
        const string json = """{"protocolVersion":1,"kind":"Hello","operation":"Hello","requestId":null,"sentAtUtc":"2026-08-13T12:00:00Z","payload":{"secret":"<strong-secret>","clientVersion":"EMA-Bot-MT5-Bridge/1","terminalInstanceId":"mt5-ABC"}}""";

        var envelope = JsonSerializer.Deserialize<Mt5BridgeEnvelope>(json, Mt5BridgeProtocol.JsonOptions);

        Assert.NotNull(envelope); Assert.Equal(new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero), envelope.SentAtUtc);
    }

    [Fact]
    public void Mql5IsoQuoteWireJson_PreservesMilliseconds()
    {
        const string json = """{"protocolVersion":1,"kind":"Response","operation":"GetQuote","requestId":"11111111-1111-1111-1111-111111111111","sentAtUtc":"2026-08-13T12:00:10Z","payload":{"brokerSymbol":"terminal.symbol","timeUtc":"2026-08-13T12:00:10.123Z","bid":100.0,"ask":100.2,"last":null,"volume":null}}""";

        var envelope = JsonSerializer.Deserialize<Mt5BridgeEnvelope>(json, Mt5BridgeProtocol.JsonOptions);
        var quote = envelope!.DeserializePayload<Mt5QuotePayload>();

        Assert.Equal(new DateTimeOffset(2026, 8, 13, 12, 0, 10, 123, TimeSpan.Zero), quote!.TimeUtc);
    }

    [Fact]
    public void OldMql5DottedTimestamp_IsRejectedByProtocolJson()
    {
        const string json = """{"protocolVersion":1,"kind":"Hello","operation":"Hello","requestId":null,"sentAtUtc":"2026.08.13 12:00:00Z","payload":null}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Mt5BridgeEnvelope>(json, Mt5BridgeProtocol.JsonOptions));
    }

    private static MemoryStream Frame(byte[] payload)
    {
        var frame = new byte[4 + payload.Length]; BinaryPrimitives.WriteUInt32LittleEndian(frame, checked((uint)payload.Length)); payload.CopyTo(frame, 4); return new MemoryStream(frame);
    }

    private sealed class ChunkedReadStream(byte[] bytes, int chunkSize) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false; public override long Length => _inner.Length; public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, chunkSize));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer[..Math.Min(buffer.Length, chunkSize)], cancellationToken);
        public override void Flush() { } public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask; public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

public sealed class Mt5BridgeOptionsAndProtocolTests
{
    [Fact]
    public void DisabledConfigurationNeedsNoSecretAndEnabledConfigurationIsValidated()
    {
        Assert.Empty(Mt5BridgeOptions.Validate(new Mt5BridgeOptions()));
        Assert.NotEmpty(Mt5BridgeOptions.Validate(new Mt5BridgeOptions { Enabled = true }));
        Assert.NotEmpty(Mt5BridgeOptions.Validate(new Mt5BridgeOptions { Enabled = true, HandshakeSecret = new string('s', 32), PipeName = "bad/name", RequestTimeoutSeconds = 0, MaxFrameBytes = 0 }));
    }

    [Fact]
    public void OperationsAreExactlyTheApprovedReadOnlySet()
    {
        var allowed = new[] { Mt5BridgeOperation.Ping, Mt5BridgeOperation.GetAccount, Mt5BridgeOperation.GetInstruments, Mt5BridgeOperation.GetInstrument, Mt5BridgeOperation.GetQuote, Mt5BridgeOperation.GetLatestBars, Mt5BridgeOperation.GetBarsRange, Mt5BridgeOperation.GetBarSnapshot, Mt5BridgeOperation.CalculateMargin, Mt5BridgeOperation.CalculateProfit };
        Assert.Equal(allowed, Mt5BridgeProtocol.AllowedRequestOperations.OrderBy(operation => operation));
        Assert.Equal(new[] { Mt5BridgeOperation.Hello, Mt5BridgeOperation.Heartbeat, Mt5BridgeOperation.Ping, Mt5BridgeOperation.GetAccount, Mt5BridgeOperation.GetInstruments, Mt5BridgeOperation.GetInstrument, Mt5BridgeOperation.GetQuote, Mt5BridgeOperation.GetLatestBars, Mt5BridgeOperation.GetBarsRange, Mt5BridgeOperation.GetBarSnapshot, Mt5BridgeOperation.CalculateMargin, Mt5BridgeOperation.CalculateProfit }, Enum.GetValues<Mt5BridgeOperation>());
    }

    [Fact]
    public async Task DisabledServerHasNoPipeAndResolvesWithSanitizedStatus()
    {
        await using var server = CreateServer(new Mt5BridgeOptions());
        await server.StartAsync(CancellationToken.None);
        var status = server.GetStatus();
        var serialized = JsonSerializer.Serialize(Mt5BridgeStatusResponse.From(status), Mt5BridgeProtocol.JsonOptions);

        Assert.Equal(Mt5BridgeConnectionState.Disabled, status.ConnectionState); Assert.False(server.IsConnected); Assert.DoesNotContain("secret", serialized, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("login", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisabledServerCanBeStoppedAndDisposedRepeatedly()
    {
        var server = CreateServer(new Mt5BridgeOptions());

        await server.StartAsync(CancellationToken.None);
        await server.StopAsync(CancellationToken.None);
        await server.DisposeAsync();
        await server.StopAsync(CancellationToken.None);
        await server.DisposeAsync();

        Assert.Equal(Mt5BridgeConnectionState.Disabled, server.GetStatus().ConnectionState);
    }

    private static Mt5BridgeServer CreateServer(Mt5BridgeOptions options) => new(Options.Create(options), TimeProvider.System, NullLogger<Mt5BridgeServer>.Instance);
}

public sealed class Mt5BridgeServerTests
{
    [Fact]
    public async Task ValidHelloOverRealNamedPipeConnectsAndSanitizesStatus()
    {
        await using var harness = await BridgeHarness.StartAsync();
        await using var client = await harness.ConnectAsync();
        var ack = await harness.HelloAsync(client);
        var status = harness.Server.GetStatus();

        Assert.Equal(Mt5BridgeFrameKind.HelloAck, ack.Kind); Assert.Equal(Mt5BridgeProtocol.ProtocolVersion, ack.DeserializePayload<Mt5HelloAckPayload>()!.ProtocolVersion);
        Assert.Equal(Mt5BridgeConnectionState.Connected, status.ConnectionState); Assert.Equal("Synthetic MT5", status.TerminalName);
        var serialized = JsonSerializer.Serialize(Mt5BridgeStatusResponse.From(status), Mt5BridgeProtocol.JsonOptions);
        Assert.DoesNotContain(harness.Options.HandshakeSecret!, serialized); Assert.DoesNotContain("terminalInstanceId", serialized, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("accountLogin", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidSecretVersionAndNonHelloFirstAreRejected()
    {
        await using var harness = await BridgeHarness.StartAsync();
        await using (var invalidSecret = await harness.ConnectAsync())
        {
            var error = await harness.HelloAsync(invalidSecret, secret: "wrong-secret");
            Assert.Equal(Mt5BridgeFrameKind.Error, error.Kind); Assert.Equal("Bridge authentication failed.", error.DeserializePayload<Mt5BridgeErrorPayload>()!.Message);
        }
        await harness.WaitForAsync(status => status.ConnectionState == Mt5BridgeConnectionState.WaitingForClient && status.LastDisconnectAtUtc is not null);
        await using (var invalidVersion = await harness.ConnectAsync())
        {
            var error = await harness.HelloAsync(invalidVersion, version: 2);
            Assert.Equal(Mt5BridgeFrameKind.Error, error.Kind);
        }
        await harness.WaitForAsync(status => status.ConnectionState == Mt5BridgeConnectionState.WaitingForClient && status.LastDisconnectAtUtc is not null);
        await using var heartbeatFirst = await harness.ConnectAsync();
        await harness.Codec.WriteAsync(heartbeatFirst, Mt5BridgeEnvelope.Create(Mt5BridgeFrameKind.Heartbeat, Mt5BridgeOperation.Heartbeat, null, new Mt5HeartbeatPayload(null), TimeProvider.System), CancellationToken.None);
        Assert.Equal(Mt5BridgeFrameKind.Error, (await harness.Codec.ReadAsync(heartbeatFirst, CancellationToken.None))!.Kind);
        await harness.WaitForAsync(status => status.ConnectionState == Mt5BridgeConnectionState.WaitingForClient);
    }

    [Fact]
    public async Task HandshakeTimeoutReturnsToWaitingState()
    {
        await using var harness = await BridgeHarness.StartAsync(handshakeSeconds: 1);
        await using var client = await harness.ConnectAsync();

        await harness.WaitForAsync(status => status.ConnectionState == Mt5BridgeConnectionState.WaitingForClient && status.LastDisconnectReason == "MT5 bridge handshake timed out.", 3_000);
    }

    [Fact]
    public async Task HeartbeatAndPureStaleCheckAreTrackedSeparately()
    {
        await using var harness = await BridgeHarness.StartAsync(heartbeatSeconds: 10);
        await using var client = await harness.ConnectAsync();
        await harness.HelloAsync(client);
        var before = harness.Server.GetStatus();
        await harness.Codec.WriteAsync(client, Mt5BridgeEnvelope.Create(Mt5BridgeFrameKind.Heartbeat, Mt5BridgeOperation.Heartbeat, null, new Mt5HeartbeatPayload(null), TimeProvider.System), CancellationToken.None);
        await harness.WaitForAsync(status => status.LastHeartbeatAtUtc is not null);
        var after = harness.Server.GetStatus();

        Assert.True(after.LastMessageAtUtc >= before.LastMessageAtUtc); Assert.NotNull(after.LastHeartbeatAtUtc); Assert.True(harness.Server.IsHeartbeatStale(after.LastHeartbeatAtUtc!.Value.AddSeconds(11)));
    }

    [Fact]
    public async Task RequestsCorrelateInReverseOrderAndWritesRemainWholeFrames()
    {
        await using var harness = await BridgeHarness.StartAsync();
        await using var client = await harness.ConnectAsync();
        await harness.HelloAsync(client);
        var first = harness.Server.SendAsync(Mt5BridgeOperation.GetAccount, null, CancellationToken.None);
        var second = harness.Server.SendAsync(Mt5BridgeOperation.GetInstruments, null, CancellationToken.None);
        var requestA = (await harness.Codec.ReadAsync(client, CancellationToken.None))!;
        var requestB = (await harness.Codec.ReadAsync(client, CancellationToken.None))!;

        Assert.Equal(Mt5BridgeFrameKind.Request, requestA.Kind); Assert.Equal(Mt5BridgeFrameKind.Request, requestB.Kind); Assert.NotEqual(requestA.RequestId, requestB.RequestId);
        await harness.Codec.WriteAsync(client, Mt5BridgeEnvelope.Create(Mt5BridgeFrameKind.Response, requestB.Operation, requestB.RequestId, new { value = "second" }, TimeProvider.System), CancellationToken.None);
        await harness.Codec.WriteAsync(client, Mt5BridgeEnvelope.Create(Mt5BridgeFrameKind.Response, requestA.Operation, requestA.RequestId, new { value = "first" }, TimeProvider.System), CancellationToken.None);

        Assert.Equal(requestA.RequestId, (await first).RequestId); Assert.Equal(requestB.RequestId, (await second).RequestId);
    }

    [Fact]
    public async Task UnknownResponseDoesNotCompleteOtherRequestAndPingRecordsRtt()
    {
        await using var harness = await BridgeHarness.StartAsync();
        await using var client = await harness.ConnectAsync();
        await harness.HelloAsync(client);
        var ping = harness.Server.SendAsync(Mt5BridgeOperation.Ping, null, CancellationToken.None);
        var request = (await harness.Codec.ReadAsync(client, CancellationToken.None))!;
        await harness.Codec.WriteAsync(client, Mt5BridgeEnvelope.Create(Mt5BridgeFrameKind.Response, Mt5BridgeOperation.Ping, Guid.NewGuid(), null, TimeProvider.System), CancellationToken.None);
        await Task.Delay(50);
        Assert.False(ping.IsCompleted);
        await harness.Codec.WriteAsync(client, Mt5BridgeEnvelope.Create(Mt5BridgeFrameKind.Response, Mt5BridgeOperation.Ping, request.RequestId, null, TimeProvider.System), CancellationToken.None);
        await ping;
        Assert.True(harness.Server.GetStatus().LastRoundTripMs >= 0);
    }

    [Fact]
    public async Task RequestTimeoutCleansPendingRequestAndDisconnectFailsItImmediately()
    {
        await using var harness = await BridgeHarness.StartAsync(requestSeconds: 1);
        var client = await harness.ConnectAsync();
        await harness.HelloAsync(client);
        var timeout = harness.Server.SendAsync(Mt5BridgeOperation.GetQuote, new Mt5GetInstrumentRequest("terminal.symbol"), CancellationToken.None);
        await harness.Codec.ReadAsync(client, CancellationToken.None);
        await Assert.ThrowsAsync<Mt5BridgeRequestTimeoutException>(() => timeout);
        Assert.Equal(0, harness.Server.PendingRequestCount);

        var pending = harness.Server.SendAsync(Mt5BridgeOperation.GetQuote, new Mt5GetInstrumentRequest("terminal.symbol"), CancellationToken.None);
        await harness.Codec.ReadAsync(client, CancellationToken.None);
        await client.DisposeAsync();
        await Assert.ThrowsAsync<Mt5BridgeDisconnectedException>(() => pending);
        await harness.WaitForAsync(status => status.ConnectionState == Mt5BridgeConnectionState.WaitingForClient);
        Assert.Equal(0, harness.Server.PendingRequestCount);
    }

    [Fact]
    public async Task SecondClientDoesNotReplaceAuthenticatedSession()
    {
        await using var harness = await BridgeHarness.StartAsync();
        await using var first = await harness.ConnectAsync();
        await harness.HelloAsync(first);
        var sessionId = harness.Server.GetStatus().SessionId;
        await using var second = new NamedPipeClientStream(".", harness.Options.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(250);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.ConnectAsync(timeout.Token));
        Assert.Equal(sessionId, harness.Server.GetStatus().SessionId); Assert.True(harness.Server.IsConnected);
    }

    [Fact]
    public async Task StopAndDisposeAreIdempotentAfterStart()
    {
        await using var harness = await BridgeHarness.StartAsync();

        await harness.Server.StopAsync(CancellationToken.None);
        await harness.Server.DisposeAsync();
        await harness.Server.StopAsync(CancellationToken.None);
        await harness.Server.DisposeAsync();

        Assert.False(harness.Server.IsConnected);
        Assert.Equal(0, harness.Server.PendingRequestCount);
    }

    [Fact]
    public async Task ConcurrentStopsAndSubsequentDisposeAreSafe()
    {
        await using var harness = await BridgeHarness.StartAsync();

        await Task.WhenAll(
            harness.Server.StopAsync(CancellationToken.None),
            harness.Server.StopAsync(CancellationToken.None));
        await harness.Server.DisposeAsync();

        Assert.False(harness.Server.IsConnected);
        Assert.Equal(0, harness.Server.PendingRequestCount);
    }

    [Fact]
    public async Task ConnectedBridgeStopsAndDisposesWithoutLeavingPendingRequests()
    {
        await using var harness = await BridgeHarness.StartAsync();
        await using var client = await harness.ConnectAsync();
        await harness.HelloAsync(client);
        Assert.True(harness.Server.IsConnected);

        await harness.Server.StopAsync(CancellationToken.None);
        await harness.Server.DisposeAsync();
        await harness.Server.DisposeAsync();

        Assert.False(harness.Server.IsConnected);
        Assert.Equal(0, harness.Server.PendingRequestCount);
    }

    private sealed class BridgeHarness : IAsyncDisposable
    {
        public Mt5BridgeOptions Options { get; }
        public Mt5BridgeServer Server { get; }
        public Mt5BridgeFrameCodec Codec { get; }

        private BridgeHarness(Mt5BridgeOptions options)
        {
            Options = options; Codec = new Mt5BridgeFrameCodec(options.MaxFrameBytes); Server = new Mt5BridgeServer(Microsoft.Extensions.Options.Options.Create(options), TimeProvider.System, NullLogger<Mt5BridgeServer>.Instance);
        }

        public static async Task<BridgeHarness> StartAsync(int handshakeSeconds = 2, int requestSeconds = 2, int heartbeatSeconds = 15)
        {
            var harness = new BridgeHarness(new Mt5BridgeOptions { Enabled = true, PipeName = $"ema-bot-tests-{Guid.NewGuid():N}", HandshakeSecret = "synthetic-test-secret-that-is-at-least-32-characters", HandshakeTimeoutSeconds = handshakeSeconds, RequestTimeoutSeconds = requestSeconds, HeartbeatTimeoutSeconds = heartbeatSeconds, MaxFrameBytes = 4_096 });
            await harness.Server.StartAsync(CancellationToken.None); return harness;
        }

        public async Task<NamedPipeClientStream> ConnectAsync()
        {
            var client = new NamedPipeClientStream(".", Options.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(2_000); return client;
        }

        public async Task<Mt5BridgeEnvelope> HelloAsync(NamedPipeClientStream client, string? secret = null, int version = Mt5BridgeProtocol.ProtocolVersion)
        {
            await Codec.WriteAsync(client, new Mt5BridgeEnvelope(version, Mt5BridgeFrameKind.Hello, Mt5BridgeOperation.Hello, Guid.NewGuid(), DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new Mt5HelloPayload(secret ?? Options.HandshakeSecret!, "test-client", "terminal-instance", "Synthetic MT5", "Synthetic Company", 5000, 123456, "Synthetic-Server", "USD", "Demo"), Mt5BridgeProtocol.JsonOptions)), CancellationToken.None);
            return (await Codec.ReadAsync(client, CancellationToken.None))!;
        }

        public async Task WaitForAsync(Func<Mt5BridgeStatus, bool> predicate, int timeoutMs = 2_000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (predicate(Server.GetStatus())) return;
                await Task.Delay(20);
            }
            Assert.True(predicate(Server.GetStatus()), "Timed out waiting for MT5 bridge state.");
        }

        public async ValueTask DisposeAsync() => await Server.DisposeAsync();
    }
}

public sealed class Mt5ExecutionBridgeServerTests
{
    [Fact]
    public async Task InvalidHandshakeDisposesPipeAndAllowsTheNextClientToConnect()
    {
        var options = new Mt5ExecutionBridgeOptions
        {
            Enabled = true,
            PipeName = $"ema-bot-execution-tests-{Guid.NewGuid():N}",
            HandshakeSecret = "synthetic-execution-secret-that-is-at-least-32-characters"
        };
        await using var server = new Mt5ExecutionBridgeServer(Options.Create(options), TimeProvider.System, NullLogger<Mt5ExecutionBridgeServer>.Instance);
        await server.StartAsync(CancellationToken.None);

        await using (var invalid = new NamedPipeClientStream(".", options.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await invalid.ConnectAsync(2_000);
            await invalid.WriteAsync(new byte[] { 0, 0, 0, 0 });
            await invalid.FlushAsync();
        }

        await using var valid = new NamedPipeClientStream(".", options.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await valid.ConnectAsync(3_000);
        await WriteAsync(valid, Mt5ExecutionEnvelope.Create(Mt5ExecutionFrameKind.Hello, Mt5ExecutionOperation.Hello, null,
            new Mt5ExecutionHelloPayload(options.HandshakeSecret, "synthetic-execution-client", "fingerprint", "Synthetic-Server", "Demo", false, false), TimeProvider.System));

        var acknowledgement = await ReadAsync(valid, CancellationToken.None);

        Assert.Equal(Mt5ExecutionFrameKind.HelloAck, acknowledgement.Kind);
        Assert.True(server.IsConnected);
        Assert.Equal("Synthetic-Server", server.GetStatus().AccountServer);
    }

    [Fact]
    public async Task ExplicitErrorResponse_UsesStructuredExecutionRejection()
    {
        var options = new Mt5ExecutionBridgeOptions { Enabled = true, PipeName = $"ema-bot-execution-tests-{Guid.NewGuid():N}", HandshakeSecret = "synthetic-execution-secret-that-is-at-least-32-characters" };
        await using var server = new Mt5ExecutionBridgeServer(Options.Create(options), TimeProvider.System, NullLogger<Mt5ExecutionBridgeServer>.Instance);
        await server.StartAsync(CancellationToken.None);
        await using var client = new NamedPipeClientStream(".", options.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(3_000);
        await WriteAsync(client, Mt5ExecutionEnvelope.Create(Mt5ExecutionFrameKind.Hello, Mt5ExecutionOperation.Hello, null, new Mt5ExecutionHelloPayload(options.HandshakeSecret, "synthetic-execution-client", "fingerprint", "Synthetic-Server", "Demo", false, false), TimeProvider.System));
        Assert.Equal(Mt5ExecutionFrameKind.HelloAck, (await ReadAsync(client, CancellationToken.None)).Kind);

        var pending = server.SendAsync(Mt5ExecutionOperation.OrderCheck, new { }, CancellationToken.None);
        var request = await ReadAsync(client, CancellationToken.None);
        await WriteAsync(client, Mt5ExecutionEnvelope.Create(Mt5ExecutionFrameKind.Error, request.Operation, request.RequestId, new Mt5ExecutionErrorPayload("DemoSafetyGate", "Synthetic gate rejection.", false, 42), TimeProvider.System));

        var exception = await Assert.ThrowsAsync<Mt5ExecutionBridgeRejectedException>(async () => await pending);
        Assert.Equal("DemoSafetyGate", exception.Code); Assert.Equal("Synthetic gate rejection.", exception.Message); Assert.False(exception.Retryable); Assert.Equal(42, exception.NativeCode);
    }

    [Fact]
    public void LegacyExecutionAccountPayload_DeserializesAsCapabilityUnsupported()
    {
        const string json = "{\"accountFingerprint\":\"test-fingerprint\",\"server\":\"test-server\",\"tradeMode\":\"Demo\",\"accountTradeAllowed\":true,\"expertTradeAllowed\":true,\"demoExecutionEnabled\":true,\"demoExecutionAllowed\":true}";
        var payload = JsonSerializer.Deserialize<Mt5ExecutionAccountPayload>(json, Mt5ExecutionBridgeProtocol.JsonOptions);

        Assert.NotNull(payload);
        Assert.Null(payload.EaBuildId);
        Assert.False(payload.SupportsExactProtectionReadback);
        Assert.False(payload.SupportsNativeExitReason);
    }

    [Fact]
    public void CurrentExecutionAccountPayload_RoundTripsCapabilities()
    {
        var account = new Mt5ExecutionAccountPayload("test-fingerprint", "test-server", "Demo", true, true, true, true, "E11.7A1A1", true, true);
        var json = JsonSerializer.Serialize(account, Mt5ExecutionBridgeProtocol.JsonOptions);
        var payload = JsonSerializer.Deserialize<Mt5ExecutionAccountPayload>(json, Mt5ExecutionBridgeProtocol.JsonOptions);

        Assert.NotNull(payload);
        Assert.Equal("test-fingerprint", payload.AccountFingerprint);
        Assert.Equal("test-server", payload.Server);
        Assert.Equal("Demo", payload.TradeMode);
        Assert.True(payload.AccountTradeAllowed);
        Assert.True(payload.ExpertTradeAllowed);
        Assert.True(payload.DemoExecutionEnabled);
        Assert.True(payload.DemoExecutionAllowed);
        Assert.Equal("E11.7A1A1", payload.EaBuildId);
        Assert.True(payload.SupportsExactProtectionReadback);
        Assert.True(payload.SupportsNativeExitReason);
    }

    private static async Task WriteAsync(Stream stream, Mt5ExecutionEnvelope envelope)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, Mt5ExecutionBridgeProtocol.JsonOptions);
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length));
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private static async Task<Mt5ExecutionEnvelope> ReadAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        await ReadExactlyAsync(stream, header, token);
        var body = new byte[BitConverter.ToInt32(header)];
        await ReadExactlyAsync(stream, body, token);
        return JsonSerializer.Deserialize<Mt5ExecutionEnvelope>(body, Mt5ExecutionBridgeProtocol.JsonOptions)!;
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), token);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }
}

public sealed class MqlExecutionBridgeContractTests
{
    [Fact]
    public void V1_ReadOnlyAdapter_HasExpectedSafetyContract()
    {
        var source = File.ReadAllText(ResolveMt5Source("EmaBotBridgeV1.mq5"));
        var dispatch = ExtractMethodBody(source, "void HandleRequest(");

        Assert.Contains("#define PROTOCOL_VERSION 1", source);
        Assert.Contains("InpPipeName=\"ema-bot.mt5.bridge.v1\"", source);
        Assert.Contains("Read-only EMA-Bot MT5 Named Pipe adapter", source);
        Assert.DoesNotContain("InpEnableDemoExecution", source);
        Assert.DoesNotContain("SubmitMarketOrder", source);
        Assert.DoesNotContain("ClosePosition", source);
        Assert.DoesNotContain("OrderCheck", source);
        Assert.DoesNotContain("GetExecutionHistory", source);
        Assert.DoesNotContain("if(operation==\"SubmitMarketOrder\"", dispatch);
        Assert.DoesNotContain("if(operation==\"ClosePosition\"", dispatch);
    }

    [Fact]
    public void V2_ExecutionAdapter_HasExpectedSafetyContract()
    {
        var source = File.ReadAllText(ResolveMt5Source("EmaBotExecutionBridgeV2.mq5"));
        var sendEnvelope = ExtractMethodBody(source, "bool SendEnvelope(");

        Assert.Contains("#define PROTOCOL_VERSION 2", source);
        Assert.Contains("InpPipeName=\"ema-bot.mt5.bridge.v2\"", source);
        Assert.Contains("InpEnableDemoExecution=false", source);
        Assert.Contains("+(string)PROTOCOL_VERSION+", sendEnvelope);
        Assert.DoesNotContain("\\\"protocolVersion\\\":1", sendEnvelope);
    }

    [Fact]
    public void AmbiguousSharedBridgeSource_IsNotPresent()
    {
        var directory = ResolveMt5EmaBotDirectory();
        var ambiguousPath = Path.Combine(directory, "EmaBotBridge.mq5");
        Assert.False(File.Exists(ambiguousPath));
    }

    private static string ResolveMt5EmaBotDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "mt5", "MQL5", "Experts", "EmaBot");
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException("Could not locate mt5/MQL5/Experts/EmaBot directory.");
    }

    private static string ResolveMt5Source(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "mt5", "MQL5", "Experts", "EmaBot", fileName);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Could not locate mt5/MQL5/Experts/EmaBot/{fileName}.");
    }



    [Fact]
    public void V2_OrderCheckSuccess_RequiresBooleanOrderCheckResult_NotTradeAcceptedRetcode()
    {
        // A successful MQL5 OrderCheck returns true. Its MqlTradeCheckResult.retcode may be
        // 0 / comment "Done", which is NOT the OrderSend acceptance criterion.
        // TRADE_RETCODE_DONE (10009) applies to MqlTradeResult from OrderSend, not OrderCheck.
        // The v2 adapter must classify OrderCheck preflight success from the boolean OrderCheck
        // return value, preserving retcode/comment only for diagnostics.
        var source = File.ReadAllText(ResolveMt5Source("EmaBotExecutionBridgeV2.mq5"));
        var handler = ExtractMethodBody(source, "void HandleExecutionOrder(");

        // OrderCheck failure path is preserved: boolean false => rejected.
        Assert.Contains("const bool checked=OrderCheck(request,check);", handler);
        Assert.Contains("if(!checked)", handler);

        // OrderCheck success path must return Accepted=true driven by the boolean, not TradeAccepted(retcode).
        Assert.Contains("if(operation==\"OrderCheck\") { SendOrderCheckResult(operation,request_id,true,check); return; }", handler);

        // The buggy form (Acceptance via TradeAccepted(check.retcode)) must no longer exist.
        Assert.DoesNotContain("TradeAccepted(check.retcode)", handler);

        // Actual OrderSend path is NOT weakened: still requires sent && TradeAccepted(result.retcode).
        Assert.Contains("const bool sent=OrderSend(request,result); const bool accepted=sent && TradeAccepted(result.retcode); SendSubmitResult(operation,request_id,accepted,result,symbol,magic,side);", handler);
    }

    [Fact]
    public void V2_NativeExecutionIdentityContract_UsesNativeFieldsAndBrokerSafeMarkers()
    {
        var source = File.ReadAllText(ResolveMt5Source("EmaBotExecutionBridgeV2.mq5"));
        var dispatch = ExtractMethodBody(source, "void HandleRequest(");
        var close = ExtractMethodBody(source, "void HandleClosePosition(");
        var position = ExtractMethodBody(source, "void SendExecutionPosition(");
        var exactDeal = ExtractMethodBody(source, "void SendExactDeal(");
        var positionHistory = ExtractMethodBody(source, "void SendPositionHistory(");
        var resolve = ExtractMethodBody(source, "ulong ResolvePositionIdentifierFromDeal(");
        var executionAccount = ExtractMethodBody(source, "void SendExecutionAccount(");

        Assert.Contains("if(operation==\"GetExactDeal\") { SendExactDeal(request_id,payload); return; }", dispatch);
        Assert.Contains("if(operation==\"GetPositionHistory\") { SendPositionHistory(request_id,payload); return; }", dispatch);
        Assert.Contains("demoExecutionEnabled", executionAccount);
        Assert.Contains("InpEnableDemoExecution", executionAccount);
        Assert.Contains("demoExecutionAllowed", executionAccount);
        Assert.Contains("DemoExecutionAllowed()", executionAccount);
        Assert.Contains("StringLen(marker)>31", source);
        Assert.Contains("HistoryDealSelect(deal_ticket)", resolve);
        Assert.True(resolve.IndexOf("HistoryDealSelect(deal_ticket)", StringComparison.Ordinal) < resolve.IndexOf("HistoryDealGetInteger(deal_ticket,DEAL_POSITION_ID)", StringComparison.Ordinal));
        Assert.Contains("POSITION_IDENTIFIER", close);
        Assert.Contains("POSITION_MAGIC", close);
        Assert.Contains("POSITION_SYMBOL", close);
        Assert.Contains("identifier<=0", close);
        Assert.Contains("actual_identifier!=identifier", close);
        Assert.DoesNotContain("POSITION_COMMENT)!=marker", close);
        Assert.Contains("POSITION_IDENTIFIER", position);
        Assert.Contains("POSITION_MAGIC", position);
        Assert.Contains("POSITION_SYMBOL", position);
        Assert.Contains("identifier<=0", position);
        Assert.Contains("actual_identifier!=identifier", position);
        Assert.DoesNotContain("POSITION_COMMENT)!=marker", position);
        Assert.Contains("expectedVolumeLots", exactDeal);
        Assert.Contains("expectedOrderTicket", exactDeal);
        Assert.Contains("actual_order!=expected_order", exactDeal);
        Assert.Contains("actual_magic!=magic", exactDeal);
        Assert.Contains("actual_symbol!=symbol", exactDeal);
        Assert.Contains("deal_side!=side", exactDeal);
        Assert.Contains("!is_entry", exactDeal);
        Assert.Contains("HistoryDealSelect((ulong)entry_deal)", positionHistory);
        Assert.True(positionHistory.IndexOf("HistoryDealSelect((ulong)entry_deal)", StringComparison.Ordinal) < positionHistory.IndexOf("HistorySelect((datetime)from,(datetime)to)", StringComparison.Ordinal));
        Assert.Contains("entry_identifier!=identifier", positionHistory);
        Assert.Contains("entry_magic!=magic", positionHistory);
        Assert.Contains("entry_symbol!=symbol", positionHistory);
        Assert.Contains("entry_side!=side", positionHistory);
        Assert.Contains("entry_semantics!=DEAL_ENTRY_IN", positionHistory);
        Assert.Contains("ResolveCurrentPositionTicket(position_id", source);
    }

    [Fact]
    public void V2_NativeExitReason_IsAdditiveExactHistoryEvidence()
    {
        var source = File.ReadAllText(ResolveMt5Source("EmaBotExecutionBridgeV2.mq5"));
        var exactDeal = ExtractMethodBody(source, "void SendExactDeal(");
        var positionHistory = ExtractMethodBody(source, "void SendPositionHistory(");
        var history = ExtractMethodBody(source, "void SendExecutionHistory(");
        var reason = ExtractMethodBody(source, "string DealReasonName(");

        Assert.Equal(2, Mt5ExecutionBridgeProtocol.ProtocolVersion);
        Assert.Contains("DEAL_REASON", exactDeal); Assert.Contains("nativeReason", exactDeal);
        Assert.Contains("DEAL_REASON", positionHistory); Assert.Contains("nativeReason", positionHistory);
        Assert.Contains("DEAL_REASON", history); Assert.Contains("nativeReason", history);
        Assert.Contains("DEAL_REASON_SL", reason); Assert.Contains("\"SL\"", reason);
        Assert.Contains("DEAL_REASON_TP", reason); Assert.Contains("\"TP\"", reason);
        Assert.Contains("DEAL_REASON_EXPERT", reason); Assert.Contains("\"Expert\"", reason);
        Assert.Contains("DEAL_REASON_SO", reason); Assert.Contains("\"StopOut\"", reason);
        Assert.Contains("Unknown:", reason);
    }

    [Fact]
    public void V2_ModifyProtection_IsExactOwnedDemoOnlyAndPreservesBothProtections()
    {
        var source = File.ReadAllText(ResolveMt5Source("EmaBotExecutionBridgeV2.mq5"));
        var dispatch = ExtractMethodBody(source, "void HandleRequest(");
        var modify = ExtractMethodBody(source, "void HandleModifyPositionProtection(");
        var result = ExtractMethodBody(source, "void SendModifyProtectionResult(");

        Assert.Contains("if(operation==\"ModifyPositionProtection\")", dispatch);
        Assert.Contains("DemoExecutionAllowed()", modify);
        Assert.Contains("positionTicket", modify); Assert.Contains("positionIdentifier", modify);
        Assert.Contains("magicNumber", modify); Assert.Contains("brokerSymbol", modify); Assert.Contains("side", modify);
        Assert.Contains("POSITION_IDENTIFIER", modify); Assert.Contains("POSITION_MAGIC", modify); Assert.Contains("POSITION_SYMBOL", modify);
        Assert.Contains("TRADE_ACTION_SLTP", modify); Assert.Contains("request.sl=sl; request.tp=tp", modify);
        Assert.Contains("SYMBOL_TRADE_STOPS_LEVEL", modify); Assert.Contains("SYMBOL_TRADE_FREEZE_LEVEL", modify);
        Assert.Contains("SYMBOL_TRADE_TICK_SIZE", modify); Assert.Contains("grid_valid", modify);
        Assert.Contains("current_sl=PositionGetDouble(POSITION_SL)", modify); Assert.Contains("current_tp=PositionGetDouble(POSITION_TP)", modify);
        Assert.Contains("monotonic_valid", modify);
        Assert.True(modify.IndexOf("current_sl=PositionGetDouble(POSITION_SL)", StringComparison.Ordinal) < modify.IndexOf("OrderCheck(request,check)", StringComparison.Ordinal));
        Assert.True(modify.IndexOf("grid_valid", StringComparison.Ordinal) < modify.IndexOf("OrderCheck(request,check)", StringComparison.Ordinal));
        Assert.DoesNotContain("POSITION_COMMENT", modify);
        Assert.Contains("if(!accepted)", result);
        Assert.Contains("PositionSelectByTicket(position)", result);
        Assert.Contains("actual_magic!=magic", result); Assert.Contains("actual_symbol!=symbol", result);
        Assert.Contains("\\\"accepted\\\":true", result); Assert.Contains("\\\"stopLoss\\\":null", result);
    }

    private static string ExtractMethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected MQL method signature '{signature}'.");
        var openBrace = source.IndexOf('{', start);
        Assert.True(openBrace >= 0);
        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) return source[start..(index + 1)];
        }

        throw new InvalidOperationException($"Could not extract MQL method body for '{signature}'.");
    }
}

public sealed class Mt5BridgeApiTests : IClassFixture<EmaBotApiFactory>
{
    private readonly EmaBotApiFactory _factory;
    public Mt5BridgeApiTests(EmaBotApiFactory factory) => _factory = factory;

    [Fact]
    public async Task StatusAndPingApiAreAdminOnlyAndTruthfulWhileDisabled()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/mt5/bridge/status")).StatusCode);
        var token = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery");
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new LoginRequest("admin", "A-strong-password-123!")) };
        login.Headers.Add("X-CSRF-TOKEN", token!.Token);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(login)).StatusCode);

        var statusResponse = await client.GetAsync("/api/mt5/bridge/status");
        var status = await statusResponse.Content.ReadFromJsonAsync<Mt5BridgeStatusResponse>(Mt5BridgeProtocol.JsonOptions);
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode); Assert.NotNull(status); Assert.False(status.Enabled); Assert.Equal(Mt5BridgeConnectionState.Disabled, status.ConnectionState);
        var pingToken = await client.GetFromJsonAsync<AntiforgeryResponse>("/api/auth/antiforgery");
        using var ping = new HttpRequestMessage(HttpMethod.Post, "/api/mt5/bridge/ping"); ping.Headers.Add("X-CSRF-TOKEN", pingToken!.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.SendAsync(ping)).StatusCode);
    }
}
