using EmaBot.Api.Controllers;

namespace EmaBot.Api.Tests;

public sealed class TradeExplorerUnitTests
{
    [Fact]
    public void MonthlyChartArithmetic_UsesCalendarMonths()
    {
        var date = new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 2, 28, 0, 0, 0, TimeSpan.Zero), BinanceIntervalMath.Shift(date, "1M", 1));
    }
}
