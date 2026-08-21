using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace EmaBot.Api.Mt5Bridge;

public sealed class Mt5ExecutionBridgeOptions
{
    public const string SectionName = "Mt5ExecutionBridge";
    public const string DefaultPipeName = "ema-bot.mt5.bridge.v2";
    public bool Enabled { get; set; }
    public string PipeName { get; set; } = DefaultPipeName;
    public string? HandshakeSecret { get; set; }
    public int RequestTimeoutSeconds { get; set; } = 8;
    public int MaxFrameBytes { get; set; } = 1_048_576;
    public static IReadOnlyList<string> Validate(Mt5ExecutionBridgeOptions options)
    {
        if (!options.Enabled) return [];
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.PipeName) || options.PipeName.Length > 128 || !Regex.IsMatch(options.PipeName, "^[A-Za-z0-9._-]+$")) errors.Add("Mt5ExecutionBridge:PipeName must be a safe logical pipe name.");
        if (string.IsNullOrWhiteSpace(options.HandshakeSecret) || options.HandshakeSecret.Length < 32) errors.Add("Mt5ExecutionBridge:HandshakeSecret must contain at least 32 characters when enabled.");
        if (options.RequestTimeoutSeconds is < 1 or > 300) errors.Add("Mt5ExecutionBridge:RequestTimeoutSeconds must be between 1 and 300.");
        if (options.MaxFrameBytes is < 1 or > 16 * 1_024 * 1_024) errors.Add("Mt5ExecutionBridge:MaxFrameBytes must be between 1 and 16777216.");
        return errors;
    }
}

public sealed class Mt5ExecutionBridgeOptionsValidator : IValidateOptions<Mt5ExecutionBridgeOptions>
{
    public ValidateOptionsResult Validate(string? name, Mt5ExecutionBridgeOptions options) => Mt5ExecutionBridgeOptions.Validate(options) is { Count: 0 } ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(Mt5ExecutionBridgeOptions.Validate(options));
}

public sealed class DemoExecutionOptions
{
    public const string SectionName = "DemoExecution";
    public bool Enabled { get; set; }
    public bool DemoOnly { get; set; } = true;
    public string ExpectedAccountFingerprint { get; set; } = string.Empty;
    public string ExpectedServer { get; set; } = string.Empty;
    public long MagicNumber { get; set; } = 20260817;
    public string CorrelationPrefix { get; set; } = "EMA";
    public static IReadOnlyList<string> Validate(DemoExecutionOptions options)
    {
        if (!options.Enabled) return [];
        var errors = new List<string>();
        if (!options.DemoOnly) errors.Add("DemoExecution:DemoOnly must remain true.");
        if (string.IsNullOrWhiteSpace(options.ExpectedAccountFingerprint)) errors.Add("DemoExecution:ExpectedAccountFingerprint is required when enabled.");
        if (string.IsNullOrWhiteSpace(options.ExpectedServer)) errors.Add("DemoExecution:ExpectedServer is required when enabled.");
        if (options.MagicNumber <= 0) errors.Add("DemoExecution:MagicNumber must be positive.");
        if (string.IsNullOrWhiteSpace(options.CorrelationPrefix) || !DemoExecutionMarker.IsPrefixValid(options.CorrelationPrefix)) errors.Add($"DemoExecution:CorrelationPrefix must be 1-{DemoExecutionMarker.MaximumPrefixLength} alphanumeric characters so every generated marker fits {DemoExecutionMarker.BrokerSafeMaxLength} broker-safe characters.");
        return errors;
    }
}

public sealed class DemoExecutionOptionsValidator : IValidateOptions<DemoExecutionOptions>
{
    public ValidateOptionsResult Validate(string? name, DemoExecutionOptions options) => DemoExecutionOptions.Validate(options) is { Count: 0 } ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(DemoExecutionOptions.Validate(options));
}

public sealed class DemoStrategyAutomationOptions
{
    public const string SectionName = "DemoStrategyAutomation";
    // This is intentionally independent of DemoExecution: both gates must be open.
    public bool Enabled { get; set; }
    public decimal FixedLots { get; set; } = .01m;
}

public sealed class DemoStrategyAutomationOptionsValidator : IValidateOptions<DemoStrategyAutomationOptions>
{
    public ValidateOptionsResult Validate(string? name, DemoStrategyAutomationOptions options)
        => options.FixedLots > 0m
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("DemoStrategyAutomation:FixedLots must be greater than zero.");
}
