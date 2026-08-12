using EmaBot.Api.Services;

namespace EmaBot.Api.Tests;

public sealed class StrategyOptimizerDateValidationTests
{
    [Theory]
    [InlineData("2026-04-01", "2026-04-30", 30, true)]
    [InlineData("2026-04-01", "2026-04-29", 29, false)]
    [InlineData("2026-05-01", "2026-06-29", 60, true)]
    [InlineData("2026-01-01", "2026-03-31", 90, true)]
    [InlineData("2026-01-01", "2026-04-01", 91, false)]
    public void InclusiveUtcCalendarDays_ValidatesDateOnlyResearchPeriods(string start, string end, int expectedDays, bool accepted)
    {
        var request = Request(start, end);
        Assert.Equal(expectedDays, StrategyOptimizationService.InclusiveUtcCalendarDays(request.StartUtc, request.EndUtc));
        if (accepted) { var normalized = StrategyOptimizationService.ValidateAndNormalize(request); Assert.Equal(request.StartUtc, normalized.StartUtc); Assert.Equal(request.EndUtc, normalized.EndUtc); }
        else Assert.Throws<ArgumentException>(() => StrategyOptimizationService.ValidateAndNormalize(request));
    }

    [Fact]
    public void DateOnlyResearchTimestamps_RemainAtStartAndInclusiveEndOfUtcDay()
    {
        var request = Request("2026-04-01", "2026-04-30");
        var normalized = StrategyOptimizationService.ValidateAndNormalize(request);
        Assert.Equal(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero), normalized.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 4, 30, 23, 59, 59, 999, TimeSpan.Zero), normalized.EndUtc);
        Assert.True(normalized.EndUtc < new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
    }

    private static StrategyOptimizerStartRequest Request(string start, string end) => new(["BTCUSDT"], ["3m"], DateTimeOffset.Parse($"{start}T00:00:00.000Z"), DateTimeOffset.Parse($"{end}T23:59:59.999Z"), new([1.1m], [0m], [0m], [true], [true], [true]));
}
