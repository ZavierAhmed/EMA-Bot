using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Services;

// This export is a forensic projection of one saved BacktestRun. It never fetches market data,
// reruns the engine, reads current settings, or reconstructs unavailable historical evidence.
public static class BacktestExcelExport
{
    public static async Task<BacktestExcelWorkbook?> CreateAsync(EmaBotDbContext database, int backtestRunId, CancellationToken token)
    {
        var run = await database.BacktestRuns.AsNoTracking()
            .Include(item => item.Trades).ThenInclude(item => item.Events)
            .SingleOrDefaultAsync(item => item.Id == backtestRunId, token);
        if (run is null) return null;

        var trades = run.Trades.OrderBy(item => item.EntryTimeUtc).ThenBy(item => item.Id).ToArray();
        var events = trades.SelectMany(trade => trade.Events.Select(item => new TradeEventRow(trade, item)))
            .OrderBy(item => item.Trade.EntryTimeUtc).ThenBy(item => item.Event.TimeUtc).ThenBy(item => item.Event.Id).ToArray();
        return new BacktestExcelWorkbook(run.Symbol, run.Interval, Workbook([
            new("SUMMARY", SummaryRows(run)),
            new("SETTINGS", SettingsRows(run)),
            new("TRADES", TradeRows(trades)),
            new("TRADE EVENTS", EventRows(events)),
            new("DIAGNOSTICS", DiagnosticRows(run, trades))
        ]));
    }

    private static IEnumerable<object?[]> SummaryRows(BacktestRun run) => Fields(
        ("Classification", "BACKTEST / SIMULATION"),
        ("BacktestRunId", run.Id), ("Status", run.Status), ("MarketDataSource", run.MarketDataSource), ("MarketDataSourceLabel", MarketDataSourceLabels.For(run.MarketDataSource)), ("Symbol", run.Symbol), ("Interval", run.Interval),
        ("RequestedStartUtc", run.RequestedStartUtc), ("RequestedEndUtc", run.RequestedEndUtc), ("ActualStartUtc", run.ActualStartUtc), ("ActualEndUtc", run.ActualEndUtc),
        ("CreatedAtUtc", run.CreatedAtUtc), ("CompletedAtUtc", run.CompletedAtUtc), ("CandleCount", run.CandleCount),
        ("TotalTrades", run.TotalTrades), ("WinningTrades", run.WinningTrades), ("LosingTrades", run.LosingTrades), ("BreakEvenTrades", run.BreakEvenTrades), ("LongTrades", run.LongTrades), ("ShortTrades", run.ShortTrades), ("WinRatePercent", run.WinRatePercent),
        ("GrossPnlUsdt", run.GrossPnlUsdt), ("TotalFeesUsdt", run.TotalFeesUsdt), ("NetPnlUsdt", run.NetPnlUsdt), ("ProfitFactor", run.ProfitFactor), ("AverageNetPnlUsdt", run.AverageNetPnlUsdt), ("AverageRMultiple", run.AverageRMultiple), ("MaxDrawdownUsdt", run.MaxDrawdownUsdt),
        ("StartingBalanceUsdt", run.StartingBalanceUsdt), ("EndingBalanceUsdt", run.EndingBalanceUsdt),
        ("Note", "Research simulation using MT5 / Exness historical market data. Economics are the BacktestRun's saved compatibility-model assumptions, not actual broker execution evidence."),
        ("HoldingMinutesDefinition", "ExitTimeUtc minus EntryTimeUtc, in minutes."),
        ("InitialRiskPriceDistanceDefinition", "abs(EntryPrice - InitialStopLoss)."),
        ("InitialRiskPercentOfEntryDefinition", "InitialRiskPriceDistance / EntryPrice * 100 when EntryPrice is non-zero."),
        ("InitialRiskAmountUsdtDefinition", "InitialRiskPriceDistance * Quantity; compatibility-model arithmetic, not broker-native evidence."),
        ("MfeInitialRDefinition", "MfePrice / InitialRiskPriceDistance when initial risk is positive."),
        ("MaeInitialRDefinition", "MaePrice / InitialRiskPriceDistance when initial risk is positive."),
        ("TargetDistancePriceDefinition", "abs(OriginalTakeProfit - EntryPrice)."));

    private static IEnumerable<object?[]> SettingsRows(BacktestRun run) => Fields(
        ("RiskReward", run.RiskReward), ("FixedOrderSizeUsdt", run.FixedOrderSizeUsdt), ("MinEmaGapPercent", run.MinEmaGapPercent), ("MaxStopDistancePercent", run.MaxStopDistancePercent),
        ("PositionSizingMode", run.PositionSizingMode), ("StartingBalanceUsdt", run.StartingBalanceUsdt), ("MarginPerTradePercent", run.MarginPerTradePercent), ("Leverage", run.Leverage),
        ("WaitForConfirmationCandle", run.WaitForConfirmationCandle), ("UseEma100Filter", run.UseEma100Filter), ("UseHtfRegimeFilter", run.UseHtfRegimeFilter),
        ("TrailingStopEnabled", run.TrailingStopEnabled), ("UseAdaptiveInitialStop", run.UseAdaptiveInitialStop),
        ("SameTrendReentryEnabled", run.SameTrendReentryEnabled), ("MaxReentryAgeBars", run.MaxReentryAgeBars),
        ("ExitOnOppositeCrossover", run.ExitOnOppositeCrossover), ("FeePercentPerSide", run.FeePercentPerSide));

    private static IEnumerable<object?[]> TradeRows(IEnumerable<BacktestTrade> trades)
    {
        var header = new object?[]
        {
            "TradeId", "Direction", "IsReentry", "CrossoverTimeUtc", "SignalTimeUtc", "EntryTimeUtc", "ExitTimeUtc", "EntryPrice", "ExitPrice", "Quantity", "EntryNotionalUsdt",
            "PositionSizingMode", "AccountEquityAtEntryUsdt", "MarginUsedUsdt", "Leverage", "InitialStopLoss", "FinalStopLoss", "StopSourceType", "StopSourceTimeUtc", "OriginalTakeProfit", "FinalTakeProfit", "TakeProfitExtended", "ExitReason", "SameCandleExitConflict",
            "EntryFeeUsdt", "ExitFeeUsdt", "TotalFeesUsdt", "GrossPnlUsdt", "NetPnlUsdt", "NetPnlPercent", "GrossRMultiple", "NetRMultiple", "MfePrice", "MfePercent", "MaePrice", "MaePercent",
            "SignalOpen", "SignalClose", "SignalEma9", "SignalEma15", "SignalEma100", "SignalGapPercent", "SignalGapState", "UseAdaptiveInitialStop", "SignalAtr14", "ReversalPowerScore", "ReversalPowerBand", "StopAnchorPrice", "StopBuffer",
            "HtfTimeframe", "SignalHtfCandleCloseTimeUtc", "SignalHtfEma100Slope20Percent", "SignalHtfAtr14Percent", "TrendRegimeCrossoverTimeUtc",
            "HoldingMinutes", "InitialRiskPriceDistance", "InitialRiskPercentOfEntry", "InitialRiskAmountUsdt", "MfeInitialR", "MaeInitialR", "TargetDistancePrice"
        };
        return new[] { header }.Concat(trades.Select(trade =>
        {
            var riskDistance = decimal.Abs(trade.EntryPrice - trade.InitialStopLoss);
            var riskPositive = riskDistance > 0m;
            return new object?[]
            {
                trade.Id, trade.Direction, trade.IsReentry, trade.CrossoverTimeUtc, trade.SignalTimeUtc, trade.EntryTimeUtc, trade.ExitTimeUtc, trade.EntryPrice, trade.ExitPrice, trade.Quantity, trade.EntryNotionalUsdt,
                trade.PositionSizingMode, trade.AccountEquityAtEntryUsdt, trade.MarginUsedUsdt, trade.Leverage, trade.InitialStopLoss, trade.FinalStopLoss, trade.StopSourceType, trade.StopSourceTimeUtc, trade.OriginalTakeProfit, trade.FinalTakeProfit, trade.TakeProfitExtended, trade.ExitReason, trade.SameCandleExitConflict,
                trade.EntryFeeUsdt, trade.ExitFeeUsdt, trade.TotalFeesUsdt, trade.GrossPnlUsdt, trade.NetPnlUsdt, trade.NetPnlPercent, trade.GrossRMultiple, trade.NetRMultiple, trade.MfePrice, trade.MfePercent, trade.MaePrice, trade.MaePercent,
                trade.SignalOpen, trade.SignalClose, trade.SignalEma9, trade.SignalEma15, trade.SignalEma100, trade.SignalGapPercent, trade.SignalGapState, trade.UseAdaptiveInitialStop, trade.SignalAtr14, trade.ReversalPowerScore, trade.ReversalPowerBand, trade.StopAnchorPrice, trade.StopBuffer,
                trade.HtfTimeframe, trade.SignalHtfCandleCloseTimeUtc, trade.SignalHtfEma100Slope20Percent, trade.SignalHtfAtr14Percent, trade.TrendRegimeCrossoverTimeUtc,
                (decimal)(trade.ExitTimeUtc - trade.EntryTimeUtc).TotalMinutes, riskDistance, trade.EntryPrice == 0m ? null : riskDistance / trade.EntryPrice * 100m, riskDistance * trade.Quantity, riskPositive ? trade.MfePrice / riskDistance : null, riskPositive ? trade.MaePrice / riskDistance : null, decimal.Abs(trade.OriginalTakeProfit - trade.EntryPrice)
            };
        }));
    }

    private static IEnumerable<object?[]> EventRows(IEnumerable<TradeEventRow> events)
    {
        var header = new object?[] { "TradeId", "Direction", "IsReentry", "EventId", "Type", "TimeUtc", "EffectiveTimeUtc", "MarketPrice", "OldStop", "NewStop", "OldTakeProfit", "NewTakeProfit", "ProgressPercent", "EntryPrice", "InitialStopLoss", "OriginalTakeProfit", "ExitPrice", "ExitReason" };
        return new[] { header }.Concat(events.Select(item => new object?[] { item.Trade.Id, item.Trade.Direction, item.Trade.IsReentry, item.Event.Id, item.Event.Type, item.Event.TimeUtc, item.Event.EffectiveTimeUtc, item.Event.MarketPrice, item.Event.OldStop, item.Event.NewStop, item.Event.OldTakeProfit, item.Event.NewTakeProfit, item.Event.ProgressPercent, item.Trade.EntryPrice, item.Trade.InitialStopLoss, item.Trade.OriginalTakeProfit, item.Trade.ExitPrice, item.Trade.ExitReason }));
    }

    private static IEnumerable<object?[]> DiagnosticRows(BacktestRun run, IReadOnlyList<BacktestTrade> trades)
    {
        var signalsTotal = run.LongSignals + run.ShortSignals;
        var normalExecutedTradeCount = trades.Count(item => !item.IsReentry);
        var reentryTradeCount = trades.Count - normalExecutedTradeCount;
        return Fields(("Classification", "Aggregate diagnostics only; BacktestRun does not persist a rejected-signal ledger."),
            ("TotalCrossovers", run.TotalCrossovers), ("LongSignals", run.LongSignals), ("ShortSignals", run.ShortSignals), ("RejectedByEma100", run.RejectedByEma100), ("RejectedByEmaGap", run.RejectedByEmaGap), ("RejectedByHtfRegime", run.RejectedByHtfRegime), ("RejectedByStopDistance", run.RejectedByStopDistance), ("RejectedByFees", run.RejectedByFees), ("ConfirmationFailed", run.ConfirmationFailed), ("InvalidStopLoss", run.InvalidStopLoss), ("SkippedWhilePositionOpen", run.SkippedWhilePositionOpen), ("NoEntryCandle", run.NoEntryCandle), ("SignalsTotal", signalsTotal), ("ExecutedTradeCount", trades.Count), ("NormalExecutedTradeCount", normalExecutedTradeCount), ("ReentryTradeCount", reentryTradeCount), ("TradeExecutionRatePercent", signalsTotal == 0 ? null : (decimal)normalExecutedTradeCount / signalsTotal * 100m), ("TradeExecutionRatePercentDefinition", "Non-reentry executed trades divided by LongSignals + ShortSignals. Re-entry executions are reported separately and do not inflate this rate."));
    }

    private static IEnumerable<object?[]> Fields(params (string Field, object? Value)[] fields) => [new object?[] { "Field", "Value" }, .. fields.Select(item => new object?[] { item.Field, item.Value })];

    private sealed record TradeEventRow(BacktestTrade Trade, BacktestTradeEvent Event);
    private sealed record Sheet(string Name, IEnumerable<object?[]> Rows);

    private static byte[] Workbook(IReadOnlyList<Sheet> sheets)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Write(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" + string.Concat(Enumerable.Range(1, sheets.Count).Select(i => $"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>")) + "</Types>");
            Write(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Write(zip, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>" + string.Concat(sheets.Select((sheet, index) => $"<sheet name=\"{SecurityElement.Escape(sheet.Name)}\" sheetId=\"{index + 1}\" r:id=\"rId{index + 1}\"/>")) + "</sheets></workbook>");
            Write(zip, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + string.Concat(Enumerable.Range(1, sheets.Count).Select(i => $"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>")) + "</Relationships>");
            foreach (var (sheet, index) in sheets.Select((sheet, index) => (sheet, index))) Write(zip, $"xl/worksheets/sheet{index + 1}.xml", Worksheet(sheet.Rows));
        }
        return stream.ToArray();
    }

    private static string Worksheet(IEnumerable<object?[]> rows)
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        foreach (var row in rows) { xml.Append("<row>"); foreach (var value in row) Cell(xml, value); xml.Append("</row>"); }
        return xml.Append("</sheetData></worksheet>").ToString();
    }

    private static void Cell(StringBuilder xml, object? value)
    {
        if (value is null) { xml.Append("<c/>"); return; }
        if (value is DateTimeOffset time) { xml.Append("<c t=\"inlineStr\"><is><t>").Append(time.ToString("O", CultureInfo.InvariantCulture)).Append("</t></is></c>"); return; }
        if (value is decimal or int) { xml.Append("<c><v>").Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append("</v></c>"); return; }
        if (value is bool flag) { xml.Append("<c t=\"b\"><v>").Append(flag ? '1' : '0').Append("</v></c>"); return; }
        xml.Append("<c t=\"inlineStr\"><is><t>").Append(SecurityElement.Escape(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)).Append("</t></is></c>");
    }

    private static void Write(ZipArchive archive, string path, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
}

public sealed record BacktestExcelWorkbook(string Symbol, string Interval, byte[] Bytes);
