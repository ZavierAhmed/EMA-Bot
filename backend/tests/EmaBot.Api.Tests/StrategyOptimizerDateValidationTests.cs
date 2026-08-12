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
        if (accepted) { var normalized = StrategyOptimizationService.ValidateAndNormalize(request, Clock); Assert.Equal(request.StartUtc, normalized.StartUtc); Assert.Equal(request.EndUtc, normalized.EndUtc); }
        else Assert.Throws<ArgumentException>(() => StrategyOptimizationService.ValidateAndNormalize(request, Clock));
    }

    [Fact]
    public void DateOnlyResearchTimestamps_RemainAtStartAndInclusiveEndOfUtcDay()
    {
        var request = Request("2026-04-01", "2026-04-30");
        var normalized = StrategyOptimizationService.ValidateAndNormalize(request, Clock);
        Assert.Equal(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero), normalized.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 4, 30, 23, 59, 59, 999, TimeSpan.Zero), normalized.EndUtc);
        Assert.True(normalized.EndUtc < new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
    }

    private static StrategyOptimizerStartRequest Request(string start, string end) => new(["BTCUSDT"], ["3m"], DateTimeOffset.Parse($"{start}T00:00:00.000Z"), DateTimeOffset.Parse($"{end}T23:59:59.999Z"), new([1.1m], [0m], [0m], [true], [true], [true]));
    private static readonly TimeProvider Clock = new FixedClock(new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("2026-07-13", "2026-08-11", true)]
    [InlineData("2026-07-14", "2026-08-12", false)]
    [InlineData("2026-07-15", "2026-08-13", false)]
    public void EndDate_MustBeACompletedUtcCalendarDay(string start, string end, bool accepted)
    {
        var action = () => StrategyOptimizationService.ValidateAndNormalize(Request(start, end), Clock);
        if (accepted) Assert.Equal(DateTimeOffset.Parse($"{end}T23:59:59.999Z"), action().EndUtc);
        else Assert.Equal("The optimizer end date must be a fully completed UTC calendar day.", Assert.Throws<ArgumentException>(action).Message);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
