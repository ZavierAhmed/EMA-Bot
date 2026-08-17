using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmaBot.Api.Mt5Bridge;

// Protocol v2 is intentionally a different logical endpoint from the read-only v1 bridge.
public static class Mt5ExecutionBridgeProtocol
{
    public const int ProtocolVersion = 2;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public static readonly IReadOnlySet<Mt5ExecutionOperation> AllowedWriteOperations = new HashSet<Mt5ExecutionOperation> { Mt5ExecutionOperation.OrderCheck, Mt5ExecutionOperation.SubmitMarketOrder, Mt5ExecutionOperation.GetPosition, Mt5ExecutionOperation.GetExecutionHistory, Mt5ExecutionOperation.ClosePosition };
}

public enum Mt5ExecutionFrameKind { Hello, HelloAck, Heartbeat, Request, Response, Error }
public enum Mt5ExecutionOperation { Hello, Heartbeat, GetExecutionAccount, OrderCheck, SubmitMarketOrder, GetPosition, GetExecutionHistory, ClosePosition }
public sealed record Mt5ExecutionEnvelope(int ProtocolVersion, Mt5ExecutionFrameKind Kind, Mt5ExecutionOperation Operation, Guid? RequestId, DateTimeOffset SentAtUtc, JsonElement? Payload)
{
    public static Mt5ExecutionEnvelope Create(Mt5ExecutionFrameKind kind, Mt5ExecutionOperation operation, Guid? requestId, object? payload, TimeProvider clock) => new(Mt5ExecutionBridgeProtocol.ProtocolVersion, kind, operation, requestId, clock.GetUtcNow(), payload is null ? null : JsonSerializer.SerializeToElement(payload, Mt5ExecutionBridgeProtocol.JsonOptions));
    public T? DeserializePayload<T>() => Payload is { } payload ? payload.Deserialize<T>(Mt5ExecutionBridgeProtocol.JsonOptions) : default;
}
public sealed record Mt5ExecutionHelloPayload(string Secret, string ClientVersion, string AccountFingerprint, string AccountServer, string AccountMode, bool AccountTradeAllowed, bool ExpertTradeAllowed);
public sealed record Mt5ExecutionErrorPayload(string Code, string Message, bool Retryable, int? NativeCode = null);
public sealed record Mt5ExecutionAccountPayload(string AccountFingerprint, string Server, string TradeMode, bool AccountTradeAllowed, bool ExpertTradeAllowed);
public sealed record Mt5OrderRequest(string ClientExecutionId, string BrokerSymbol, string Side, decimal VolumeLots, decimal? StopLoss, decimal? TakeProfit, long MagicNumber, string CorrelationMarker, long? PositionTicket = null);
public sealed record Mt5OrderCheckPayload(bool Accepted, string? Retcode, string? Message, decimal? Bid, decimal? Ask);
public sealed record Mt5OrderResultPayload(bool Accepted, string? Retcode, string? Message, long? PositionTicket, long? DealTicket, decimal? FilledVolumeLots, decimal? AverageFillPrice, bool IsClosed = false, long? OrderTicket = null, long? PositionIdentifier = null, bool IsPartial = false, bool IsPositionOpen = false);
public sealed record Mt5ExecutionPositionRequest(long PositionTicket, long MagicNumber, string CorrelationMarker);
public sealed record Mt5ExecutionHistoryRequest(string ClientExecutionId, long MagicNumber, string CorrelationMarker, string BrokerSymbol, string Side, decimal ExpectedVolumeLots, long FromUnixSeconds, long ToUnixSeconds, long? KnownPositionTicket = null);
public sealed record Mt5ExecutionHistoryEvidence(long? OrderTicket, long? DealTicket, long? PositionIdentifier, long? PositionTicket, string BrokerSymbol, string Side, long MagicNumber, string CorrelationMarker, decimal ExecutedVolumeLots, decimal? ExecutionPrice, DateTimeOffset ExecutedAtUtc, string EntryType, string DealState, bool IsEntry, bool IsExit, bool IsPartial, string? NativeCode = null);
public sealed record Mt5ExecutionHistoryPayload(IReadOnlyList<Mt5ExecutionHistoryEvidence> Evidence);
public sealed class Mt5ExecutionBridgeException(string message) : Exception(message);
public sealed class Mt5ExecutionBridgeUnavailableException(string message) : InvalidOperationException(message);
public sealed class Mt5ExecutionBridgeAmbiguousException(string message) : TimeoutException(message);
