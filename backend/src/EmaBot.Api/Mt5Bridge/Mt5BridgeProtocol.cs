using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmaBot.Api.Mt5Bridge;

public static class Mt5BridgeProtocol
{
    public const int ProtocolVersion = 1;
    public static readonly IReadOnlySet<Mt5BridgeOperation> AllowedRequestOperations = new HashSet<Mt5BridgeOperation>
    {
        Mt5BridgeOperation.Ping, Mt5BridgeOperation.GetAccount, Mt5BridgeOperation.GetInstruments, Mt5BridgeOperation.GetInstrument, Mt5BridgeOperation.GetQuote, Mt5BridgeOperation.GetLatestBars, Mt5BridgeOperation.GetBarsRange, Mt5BridgeOperation.GetBarSnapshot
    };
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}

public enum Mt5BridgeFrameKind { Hello, HelloAck, Heartbeat, Request, Response, Error }
public enum Mt5BridgeOperation { Hello, Heartbeat, Ping, GetAccount, GetInstruments, GetInstrument, GetQuote, GetLatestBars, GetBarsRange, GetBarSnapshot }

public sealed record Mt5BridgeEnvelope(int ProtocolVersion, Mt5BridgeFrameKind Kind, Mt5BridgeOperation Operation, Guid? RequestId, DateTimeOffset SentAtUtc, JsonElement? Payload)
{
    public static Mt5BridgeEnvelope Create(Mt5BridgeFrameKind kind, Mt5BridgeOperation operation, Guid? requestId, object? payload, TimeProvider timeProvider)
        => new(Mt5BridgeProtocol.ProtocolVersion, kind, operation, requestId, timeProvider.GetUtcNow(), payload is null ? null : JsonSerializer.SerializeToElement(payload, Mt5BridgeProtocol.JsonOptions));

    public T? DeserializePayload<T>() => Payload is { } payload ? payload.Deserialize<T>(Mt5BridgeProtocol.JsonOptions) : default;
}

public sealed record Mt5HelloPayload(string Secret, string ClientVersion, string TerminalInstanceId, string? TerminalName, string? TerminalCompany, int? TerminalBuild, long? AccountLogin, string? AccountServer, string? AccountCurrency, string? AccountMode);
public sealed record Mt5HelloAckPayload(int ProtocolVersion, Guid SessionId, string ServerVersion, DateTimeOffset ConnectedAtUtc, int HeartbeatTimeoutSeconds);
public sealed record Mt5HeartbeatPayload(DateTimeOffset? ClientTimeUtc);
public sealed record Mt5BridgeErrorPayload(string Code, string Message, bool Retryable, int? NativeCode = null);
public sealed record Mt5AccountPayload(long Login, string Server, string Currency, decimal Balance, decimal Equity, decimal Margin, decimal FreeMargin, decimal MarginLevel, string TradeMode);
public sealed record Mt5InstrumentSpecPayload(string BrokerSymbol, string DisplaySymbol, string AssetClass, int Digits, decimal PointSize, decimal ContractSize, decimal VolumeMin, decimal VolumeMax, decimal VolumeStep, decimal? TickSize, decimal? TickValueProfit, decimal? TickValueLoss, decimal? VolumeLimit, int? StopsLevelPoints, int? FreezeLevelPoints, string? CurrencyBase, string? CurrencyProfit, string? CurrencyMargin);
public sealed record Mt5InstrumentCatalogItemPayload(Mt5InstrumentSpecPayload Spec, string? Description, string? Path, bool IsSelected, bool IsVisible, string TradeMode);
public sealed record Mt5GetInstrumentRequest(string BrokerSymbol);
public sealed record Mt5QuotePayload(string BrokerSymbol, DateTimeOffset TimeUtc, decimal Bid, decimal Ask, decimal? Last, decimal? Volume);
public sealed record Mt5GetLatestBarsRequest(string BrokerSymbol, string Timeframe, int Count);
public sealed record Mt5GetBarsRangeRequest(string BrokerSymbol, string Timeframe, long StartUnixSeconds, long EndUnixSeconds);
public sealed record Mt5GetBarSnapshotRequest(string BrokerSymbol, string Timeframe);
public sealed record Mt5BarPayload(string BrokerSymbol, string Timeframe, DateTimeOffset OpenTimeUtc, decimal Open, decimal High, decimal Low, decimal Close, long TickVolume, long RealVolume, int SpreadPoints, bool IsCurrent);
public sealed record Mt5BarSnapshotPayload(string BrokerSymbol, string Timeframe, DateTimeOffset EventTimeUtc, Mt5BarPayload PreviousClosed, Mt5BarPayload Current);

public sealed class Mt5BridgeProtocolException(string message) : Exception(message);
public sealed class Mt5BridgeUnavailableException(string message) : InvalidOperationException(message);
public sealed class Mt5BridgeRequestTimeoutException(string message) : TimeoutException(message);
public sealed class Mt5BridgeDisconnectedException(string message) : IOException(message);
public sealed class Mt5BridgeRemoteException(string code, string message, bool retryable) : Exception(message)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}
