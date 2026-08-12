using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace EmaBot.Api.Mt5Bridge;

public sealed class Mt5BridgeOptions
{
    public const string SectionName = "Mt5Bridge";
    public const string DefaultPipeName = "ema-bot.mt5.bridge.v1";
    public bool Enabled { get; set; }
    public string PipeName { get; set; } = DefaultPipeName;
    public string? HandshakeSecret { get; set; }
    public int HandshakeTimeoutSeconds { get; set; } = 5;
    public int RequestTimeoutSeconds { get; set; } = 5;
    public int HeartbeatTimeoutSeconds { get; set; } = 15;
    public int MaxFrameBytes { get; set; } = 1_048_576;

    public static IReadOnlyList<string> Validate(Mt5BridgeOptions options)
    {
        if (!options.Enabled) return [];
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.PipeName) || options.PipeName.Length > 128 || !Regex.IsMatch(options.PipeName, "^[A-Za-z0-9._-]+$")) errors.Add("Mt5Bridge:PipeName must be a safe logical pipe name.");
        if (string.IsNullOrWhiteSpace(options.HandshakeSecret) || options.HandshakeSecret.Length < 32) errors.Add("Mt5Bridge:HandshakeSecret must contain at least 32 characters when the bridge is enabled.");
        if (options.HandshakeTimeoutSeconds is < 1 or > 300) errors.Add("Mt5Bridge:HandshakeTimeoutSeconds must be between 1 and 300.");
        if (options.RequestTimeoutSeconds is < 1 or > 300) errors.Add("Mt5Bridge:RequestTimeoutSeconds must be between 1 and 300.");
        if (options.HeartbeatTimeoutSeconds is < 1 or > 3_600) errors.Add("Mt5Bridge:HeartbeatTimeoutSeconds must be between 1 and 3600.");
        if (options.MaxFrameBytes is < 1 or > 16 * 1_024 * 1_024) errors.Add("Mt5Bridge:MaxFrameBytes must be between 1 and 16777216.");
        return errors;
    }
}

public sealed class Mt5BridgeOptionsValidator : IValidateOptions<Mt5BridgeOptions>
{
    public ValidateOptionsResult Validate(string? name, Mt5BridgeOptions options)
    {
        var errors = Mt5BridgeOptions.Validate(options);
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
