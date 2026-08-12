namespace EmaBot.Api.Mt5Bridge;

public enum Mt5BridgeConnectionState { Disabled, WaitingForClient, Handshaking, Connected, Stale, Faulted }

public sealed record Mt5BridgeStatus(
    bool Enabled,
    int ProtocolVersion,
    string PipeName,
    Mt5BridgeConnectionState ConnectionState,
    Guid? SessionId,
    DateTimeOffset? ConnectedAtUtc,
    DateTimeOffset? LastMessageAtUtc,
    DateTimeOffset? LastHeartbeatAtUtc,
    DateTimeOffset? LastDisconnectAtUtc,
    string? LastDisconnectReason,
    string? ClientVersion,
    string? TerminalInstanceId,
    string? TerminalName,
    string? TerminalCompany,
    int? TerminalBuild,
    string? AccountServer,
    string? AccountCurrency,
    string? AccountMode,
    long? LastRoundTripMs);

public sealed record Mt5BridgeStatusResponse(
    bool Enabled,
    int ProtocolVersion,
    string PipeName,
    Mt5BridgeConnectionState ConnectionState,
    Guid? SessionId,
    DateTimeOffset? ConnectedAtUtc,
    DateTimeOffset? LastMessageAtUtc,
    DateTimeOffset? LastHeartbeatAtUtc,
    DateTimeOffset? LastDisconnectAtUtc,
    string? LastDisconnectReason,
    string? ClientVersion,
    string? TerminalName,
    string? TerminalCompany,
    int? TerminalBuild,
    string? AccountServer,
    string? AccountCurrency,
    string? AccountMode,
    long? LastRoundTripMs)
{
    public static Mt5BridgeStatusResponse From(Mt5BridgeStatus status) => new(status.Enabled, status.ProtocolVersion, status.PipeName, status.ConnectionState, status.SessionId, status.ConnectedAtUtc, status.LastMessageAtUtc, status.LastHeartbeatAtUtc, status.LastDisconnectAtUtc, status.LastDisconnectReason, status.ClientVersion, status.TerminalName, status.TerminalCompany, status.TerminalBuild, status.AccountServer, status.AccountCurrency, status.AccountMode, status.LastRoundTripMs);
}

public interface IMt5BridgeRequestClient
{
    bool IsConnected { get; }
    Mt5BridgeStatus GetStatus();
    Task<Mt5BridgeEnvelope> SendAsync(Mt5BridgeOperation operation, object? payload, CancellationToken cancellationToken);
}
