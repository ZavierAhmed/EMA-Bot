using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmaBot.Api.Mt5Bridge;

// Protocol v2 is intentionally a different logical endpoint from the read-only v1 bridge.
public static class Mt5ExecutionBridgeProtocol
{
    public const int ProtocolVersion = 2;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public static readonly IReadOnlySet<Mt5ExecutionOperation> AllowedWriteOperations = new HashSet<Mt5ExecutionOperation> { Mt5ExecutionOperation.OrderCheck, Mt5ExecutionOperation.SubmitMarketOrder, Mt5ExecutionOperation.GetPosition, Mt5ExecutionOperation.GetExecutionHistory, Mt5ExecutionOperation.GetExactDeal, Mt5ExecutionOperation.GetPositionHistory, Mt5ExecutionOperation.ClosePosition, Mt5ExecutionOperation.ModifyPositionProtection };
}

public enum Mt5ExecutionFrameKind { Hello, HelloAck, Heartbeat, Request, Response, Error }
public enum Mt5ExecutionOperation { Hello, Heartbeat, GetExecutionAccount, OrderCheck, SubmitMarketOrder, GetPosition, GetExecutionHistory, GetExactDeal, GetPositionHistory, ClosePosition, ModifyPositionProtection }
public sealed record Mt5ExecutionEnvelope(int ProtocolVersion, Mt5ExecutionFrameKind Kind, Mt5ExecutionOperation Operation, Guid? RequestId, DateTimeOffset SentAtUtc, JsonElement? Payload)
{
    public static Mt5ExecutionEnvelope Create(Mt5ExecutionFrameKind kind, Mt5ExecutionOperation operation, Guid? requestId, object? payload, TimeProvider clock) => new(Mt5ExecutionBridgeProtocol.ProtocolVersion, kind, operation, requestId, clock.GetUtcNow(), payload is null ? null : JsonSerializer.SerializeToElement(payload, Mt5ExecutionBridgeProtocol.JsonOptions));
    public T? DeserializePayload<T>() => Payload is { } payload ? payload.Deserialize<T>(Mt5ExecutionBridgeProtocol.JsonOptions) : default;
}
public sealed record Mt5ExecutionHelloPayload(string Secret, string ClientVersion, string AccountFingerprint, string AccountServer, string AccountMode, bool AccountTradeAllowed, bool ExpertTradeAllowed);
public sealed record Mt5ExecutionErrorPayload(string Code, string Message, bool Retryable, int? NativeCode = null);
public sealed record Mt5ExecutionAccountPayload(string AccountFingerprint, string Server, string TradeMode, bool AccountTradeAllowed, bool ExpertTradeAllowed, bool DemoExecutionEnabled = false, bool DemoExecutionAllowed = false);
public sealed record Mt5OrderRequest(string ClientExecutionId, string BrokerSymbol, string Side, decimal VolumeLots, decimal? StopLoss, decimal? TakeProfit, long MagicNumber, string CorrelationMarker, long? PositionTicket = null);
public sealed record Mt5OrderCheckPayload(bool Accepted, string? Retcode, string? Message, decimal? Bid, decimal? Ask);
public sealed record Mt5OrderResultPayload(bool Accepted, string? Retcode, string? Message, long? PositionTicket, long? DealTicket, decimal? FilledVolumeLots, decimal? AverageFillPrice, bool IsClosed = false, long? OrderTicket = null, long? PositionIdentifier = null, bool IsPartial = false, bool IsPositionOpen = false);
public sealed record Mt5ExecutionPositionRequest(long PositionTicket, long PositionIdentifier, long MagicNumber, string BrokerSymbol, string Side);
public sealed record Mt5ExecutionPositionPayload(bool Accepted, bool IsClosed, long? PositionTicket, long? PositionIdentifier, long? MagicNumber, string? BrokerSymbol, string? Side, decimal? VolumeLots, decimal? OpenPrice, string? NativeComment = null, decimal? StopLoss = null, decimal? TakeProfit = null, decimal? Bid = null, decimal? Ask = null, int? Digits = null, decimal? TickSize = null, decimal? PointSize = null, int? StopsLevelPoints = null, int? FreezeLevelPoints = null);
public sealed record Mt5ExecutionHistoryRequest(string ClientExecutionId, long MagicNumber, string CorrelationMarker, string BrokerSymbol, string Side, decimal ExpectedVolumeLots, long FromUnixSeconds, long ToUnixSeconds, long? KnownPositionTicket = null);
public sealed record Mt5ExecutionHistoryEvidence(long? OrderTicket, long? DealTicket, long? PositionIdentifier, long? PositionTicket, string BrokerSymbol, string Side, long MagicNumber, string CorrelationMarker, decimal ExecutedVolumeLots, decimal? ExecutionPrice, DateTimeOffset ExecutedAtUtc, string EntryType, string DealState, bool IsEntry, bool IsExit, bool IsPartial, string? NativeCode = null, string? NativeReason = null);
public sealed record Mt5ExecutionHistoryPayload(IReadOnlyList<Mt5ExecutionHistoryEvidence> Evidence);

// Purpose-specific native contracts.  Every field in a response is an actual broker-read
// value; request expectations are never echoed back as evidence.
public sealed record Mt5ExactDealRequest(long ExactDealTicket, long MagicNumber, string BrokerSymbol, string Side, decimal ExpectedVolumeLots, long? ExpectedOrderTicket = null);
public sealed record Mt5ExactDealPayload(long DealTicket, long? OrderTicket, long? PositionIdentifier, long? PositionTicket, string BrokerSymbol, string Side, long MagicNumber, decimal ExecutedVolumeLots, decimal? ExecutionPrice, DateTimeOffset ExecutedAtUtc, bool IsEntry, bool IsExit, bool IsPositionOpen, string? NativeComment = null, string? NativeReason = null);
public sealed record Mt5PositionHistoryRequest(long PositionIdentifier, long EntryDealTicket, long MagicNumber, string BrokerSymbol, string Side, decimal ExpectedVolumeLots, long FromUnixSeconds, long ToUnixSeconds);
public sealed record Mt5PositionHistoryDeal(long DealTicket, long? OrderTicket, long PositionIdentifier, string BrokerSymbol, string Side, long MagicNumber, decimal ExecutedVolumeLots, decimal? ExecutionPrice, DateTimeOffset ExecutedAtUtc, string EntryType, bool IsEntry, bool IsExit, string? NativeComment = null, string? NativeReason = null);
public sealed record Mt5PositionHistoryPayload(long PositionIdentifier, IReadOnlyList<Mt5PositionHistoryDeal> Deals);
public sealed record Mt5ClosePositionRequest(long PositionTicket, long PositionIdentifier, long MagicNumber, string BrokerSymbol, string Side);
public sealed record Mt5ModifyPositionProtectionRequest(long PositionTicket, long PositionIdentifier, long MagicNumber, string BrokerSymbol, string Side, decimal StopLoss, decimal TakeProfit);
public sealed record Mt5SubmitOrderResultPayload(bool Accepted, string? Retcode, string? Message, long? OrderTicket, long? EntryDealTicket, long? PositionIdentifier, long? PositionTicket, decimal? FilledVolumeLots, decimal? AverageFillPrice, bool IsPartial = false, bool IsPositionOpen = false);
public sealed record Mt5ClosePositionResultPayload(bool Accepted, string? Retcode, string? Message, long? ExitDealTicket, decimal? ClosedVolumeLots, decimal? AverageClosePrice, bool IsClosed = false);
// Values in this payload are re-read from the native position after the MT5 operation.
public sealed record Mt5ModifyPositionProtectionResultPayload(bool Accepted, string? Retcode, string? Message, long? PositionTicket, long? PositionIdentifier, decimal? StopLoss, decimal? TakeProfit);
public sealed class Mt5ExecutionBridgeException(string message) : Exception(message);
public sealed class Mt5ExecutionBridgeRejectedException(string code, string message, bool retryable, int? nativeCode = null) : Exception(message)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
    public int? NativeCode { get; } = nativeCode;
}
public sealed class Mt5ExecutionBridgeUnavailableException(string message) : InvalidOperationException(message);
public sealed class Mt5ExecutionBridgeAmbiguousException(string message) : TimeoutException(message);
