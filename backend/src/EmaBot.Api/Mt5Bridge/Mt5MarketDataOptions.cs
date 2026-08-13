using Microsoft.Extensions.Options;

namespace EmaBot.Api.Mt5Bridge;

public sealed class Mt5MarketDataOptions
{
    public const string SectionName = "Mt5MarketData";
    public int PollMilliseconds { get; set; } = 250;
}

public sealed class Mt5MarketDataOptionsValidator : IValidateOptions<Mt5MarketDataOptions>
{
    public ValidateOptionsResult Validate(string? name, Mt5MarketDataOptions options)
        => options.PollMilliseconds is >= 100 and <= 10_000 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail("Mt5MarketData:PollMilliseconds must be between 100 and 10000.");
}
