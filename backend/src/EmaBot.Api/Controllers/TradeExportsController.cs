using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using EmaBot.Api.Auth;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using EmaBot.Api.Strategy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Controllers;

[ApiController, Authorize(Roles = AppRoles.Admin), Route("api/trade-exports")]
public sealed class TradeExportsController(EmaBotDbContext database) : ControllerBase
{
    [HttpGet("excel")]
    public async Task<IActionResult> Excel([FromQuery] string? source, [FromQuery] string? symbol, [FromQuery] string? interval, [FromQuery] string? direction, [FromQuery] string? outcome, CancellationToken token)
    {
        if (!ValidFilters(source, direction, outcome)) return BadRequest(new ApiMessage("Source, direction, or outcome filter is invalid."));
        var rows = await RowsAsync(source, symbol, interval, direction, outcome, token);
        return File(SimpleExports.Xlsx(rows), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ema-bot-trades.xlsx");
    }

    [HttpGet("{source}/{id:int}/pdf")]
    public async Task<IActionResult> Pdf(string source, int id, CancellationToken token)
    {
        if (!ValidFilters(source, null, null)) return BadRequest(new ApiMessage("Source filter is invalid."));
        var row = (await RowsAsync(source, null, null, null, null, token)).SingleOrDefault(item => item.Id == id && string.Equals(item.Source, source, StringComparison.OrdinalIgnoreCase));
        return row is null ? NotFound(new ApiMessage("Trade not found.")) : File(SimpleExports.Pdf(row), "application/pdf", $"ema-bot-trade-{row.Source.ToLowerInvariant()}-{id}.pdf");
    }

    private async Task<List<TradeExportRow>> RowsAsync(string? source, string? symbol, string? interval, string? direction, string? outcome, CancellationToken token)
    {
        var rows = new List<TradeExportRow>(); var requested = source?.Trim(); var all = string.IsNullOrWhiteSpace(requested) || string.Equals(requested, "All", StringComparison.OrdinalIgnoreCase); var normalized = string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim();
        if (all || string.Equals(requested, "Backtest", StringComparison.OrdinalIgnoreCase))
        {
            var query = database.BacktestTrades.AsNoTracking().Include(item => item.BacktestRun).Include(item => item.Events).AsQueryable();
            if (normalized is not null) query = query.Where(item => item.BacktestRun!.Symbol == normalized);
            if (!string.IsNullOrWhiteSpace(interval)) query = query.Where(item => item.BacktestRun!.Interval == interval);
            if (!string.IsNullOrWhiteSpace(direction) && Enum.TryParse<SignalDirection>(direction, true, out var parsed)) query = query.Where(item => item.Direction == parsed);
            rows.AddRange((await query.ToListAsync(token)).Select(item => TradeExportRow.From(item)));
        }
        if (all || string.Equals(requested, "Paper", StringComparison.OrdinalIgnoreCase))
        {
            var query = database.PaperTrades.AsNoTracking().Include(item => item.PaperSession).Include(item => item.Events).AsQueryable();
            if (normalized is not null) query = query.Where(item => item.Symbol == normalized);
            if (!string.IsNullOrWhiteSpace(interval)) query = query.Where(item => item.Interval == interval);
            if (!string.IsNullOrWhiteSpace(direction) && Enum.TryParse<SignalDirection>(direction, true, out var parsed)) query = query.Where(item => item.Direction == parsed);
            rows.AddRange((await query.ToListAsync(token)).Select(item => TradeExportRow.From(item)));
        }
        return rows.Where(item => string.IsNullOrWhiteSpace(outcome) || outcome.Equals("All", StringComparison.OrdinalIgnoreCase) || outcome.Equals("Open", StringComparison.OrdinalIgnoreCase) && item.Status == "Open" || outcome.Equals("Win", StringComparison.OrdinalIgnoreCase) && item.NetPnl > 0 || outcome.Equals("Loss", StringComparison.OrdinalIgnoreCase) && item.NetPnl < 0 || outcome is "BreakEven" or "Break-even" && item.Status == "Closed" && item.NetPnl == 0).OrderByDescending(item => item.EntryTime).ToList();
    }

    private static bool ValidFilters(string? source, string? direction, string? outcome) => (string.IsNullOrWhiteSpace(source) || source.Equals("All", StringComparison.OrdinalIgnoreCase) || source.Equals("Backtest", StringComparison.OrdinalIgnoreCase) || source.Equals("Paper", StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrWhiteSpace(direction) || direction.Equals("All", StringComparison.OrdinalIgnoreCase) || direction.Equals("Long", StringComparison.OrdinalIgnoreCase) || direction.Equals("Short", StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrWhiteSpace(outcome) || outcome.Equals("All", StringComparison.OrdinalIgnoreCase) || outcome.Equals("Open", StringComparison.OrdinalIgnoreCase) || outcome.Equals("Win", StringComparison.OrdinalIgnoreCase) || outcome.Equals("Loss", StringComparison.OrdinalIgnoreCase) || outcome.Equals("BreakEven", StringComparison.OrdinalIgnoreCase) || outcome.Equals("Break-even", StringComparison.OrdinalIgnoreCase));
}

public sealed record TradeExportRow(int Id, string Source, string Symbol, string Interval, string Direction, string Status, DateTimeOffset CrossoverTime, DateTimeOffset SignalTime, DateTimeOffset EntryTime, DateTimeOffset? ExitTime, decimal Entry, decimal? Exit, decimal InitialStop, decimal FinalStop, decimal OriginalTarget, decimal FinalTarget, decimal? Margin, decimal? Leverage, decimal Notional, decimal Quantity, decimal GrossPnl, decimal Fees, decimal NetPnl, decimal NetPercent, decimal? MarginReturnPercent, decimal? R, string? ExitReason, decimal? SignalOpen, decimal SignalClose, decimal? Ema9, decimal? Ema15, decimal? Ema100, decimal? GapPercent, bool IsReentry, StopSourceType StopSource, DateTimeOffset StopSourceTime, IReadOnlyList<string> ManagementTimeline, int ParentId, decimal RiskReward, bool WaitForConfirmation, bool UseEma100, decimal MinEmaGap, decimal MaxStopDistance, string PositionSizingMode, decimal StartingBalance, decimal MarginPercent, decimal FeePercent, string? HtfTimeframe = null, DateTimeOffset? SignalHtfCandleCloseTimeUtc = null, decimal? SignalHtfEma100Slope20Percent = null, decimal? SignalHtfAtr14Percent = null)
{
    public MarketDataSource MarketDataSource { get; init; } = MarketDataSource.LegacyBinance;
    public string AccountCurrency { get; init; } = "USDT";
    public decimal? Lots { get; init; }
    public decimal? RequiredMargin { get; init; }
    public decimal? MarginUsed { get; init; }
    public decimal? AccountEquityAtEntry { get; init; }
    public decimal? EntryBid { get; init; }
    public decimal? EntryAsk { get; init; }
    public decimal? EntrySpread { get; init; }
    public decimal? ExitBid { get; init; }
    public decimal? ExitAsk { get; init; }
    public decimal? ExitSpread { get; init; }
    public string PnlPercentBasis { get; init; } = "EntryNotional";
    public bool UseAdaptiveInitialStop { get; init; }
    public decimal? ReversalPowerScore { get; init; }
    public string? ReversalPowerBand { get; init; }
    public decimal? SignalAtr14 { get; init; }
    public decimal? StopAnchorPrice { get; init; }
    public decimal? StopBuffer { get; init; }
    public static TradeExportRow From(BacktestTrade item) { var run = item.BacktestRun!; return new(item.Id, "Backtest", run.Symbol, run.Interval, item.Direction.ToString(), "Closed", item.CrossoverTimeUtc, item.SignalTimeUtc, item.EntryTimeUtc, item.ExitTimeUtc, item.EntryPrice, item.ExitPrice, item.InitialStopLoss, item.FinalStopLoss, item.OriginalTakeProfit, item.FinalTakeProfit, item.MarginUsedUsdt, item.Leverage, item.EntryNotionalUsdt, item.Quantity, item.GrossPnlUsdt, item.TotalFeesUsdt, item.NetPnlUsdt, item.NetPnlPercent, item.MarginUsedUsdt is > 0 ? item.NetPnlUsdt / item.MarginUsedUsdt * 100 : null, item.NetRMultiple, item.ExitReason.ToString(), item.SignalOpen, item.SignalClose, item.SignalEma9, item.SignalEma15, item.SignalEma100, item.SignalGapPercent, item.IsReentry, item.StopSourceType, item.StopSourceTimeUtc, item.Events.OrderBy(e => e.TimeUtc).Select(e => $"{e.TimeUtc:O} {e.Type}; price {e.MarketPrice}; stop {e.OldStop}->{e.NewStop}; target {e.OldTakeProfit}->{e.NewTakeProfit}").ToList(), run.Id, run.RiskReward, run.WaitForConfirmationCandle, run.UseEma100Filter, run.MinEmaGapPercent, run.MaxStopDistancePercent, run.PositionSizingMode.ToString(), run.StartingBalanceUsdt, run.MarginPerTradePercent, run.FeePercentPerSide, item.HtfTimeframe, item.SignalHtfCandleCloseTimeUtc, item.SignalHtfEma100Slope20Percent, item.SignalHtfAtr14Percent) { MarketDataSource = run.MarketDataSource }; }
    public static TradeExportRow From(PaperTrade item) { var session = item.PaperSession!; var events = item.Events.OrderBy(e => e.TimeUtc).Select(e => $"{e.TimeUtc:O} {e.Type}; price {e.MarketPrice}; stop {e.OldStop}->{e.NewStop}; target {e.OldTakeProfit}->{e.NewTakeProfit}").ToList(); if (session.MarketDataSource == MarketDataSource.Mt5Exness) return new(item.Id, "Paper", item.Symbol, item.Interval, item.Direction.ToString(), item.Status.ToString(), item.CrossoverTimeUtc, item.SignalTimeUtc, item.EntryTimeUtc, item.ExitTimeUtc, item.EntryPrice, item.ExitPrice, item.InitialStopLoss, item.FinalStopLoss ?? item.CurrentStopLoss, item.OriginalTakeProfit, item.FinalTakeProfit ?? item.CurrentTakeProfit, item.RequiredMargin, null, 0m, item.Quantity, item.GrossPnl ?? 0m, item.RoundTripCommission ?? 0m, item.NetPnl ?? 0m, item.NetPnlPercent, null, null, item.ExitReason?.ToString(), item.SignalOpen, item.SignalClose, item.SignalEma9, item.SignalEma15, item.SignalEma100, item.SignalGapPercent, item.IsReentry, item.StopSourceType, item.StopSourceTimeUtc, events, session.Id, session.RiskReward, session.WaitForConfirmationCandle, session.UseEma100Filter, session.MinEmaGapPercent, session.MaxStopDistancePercent, session.PaperPositionSizingMode.ToString(), session.StartingBalance, session.PaperMarginPerTradePercent, 0m) { MarketDataSource = session.MarketDataSource, AccountCurrency = session.AccountCurrency, Lots = item.Lots, RequiredMargin = item.RequiredMargin, MarginUsed = item.MarginUsed, AccountEquityAtEntry = item.AccountEquityAtEntry, EntryBid = item.EntryBid, EntryAsk = item.EntryAsk, EntrySpread = item.EntrySpread, ExitBid = item.ExitBid, ExitAsk = item.ExitAsk, ExitSpread = item.ExitSpread, PnlPercentBasis = "AccountEquityAtEntry" }; var risk = Math.Abs(item.EntryPrice - item.InitialStopLoss) * item.Quantity; return new(item.Id, "Paper", item.Symbol, item.Interval, item.Direction.ToString(), item.Status.ToString(), item.CrossoverTimeUtc, item.SignalTimeUtc, item.EntryTimeUtc, item.ExitTimeUtc, item.EntryPrice, item.ExitPrice, item.InitialStopLoss, item.FinalStopLoss ?? item.CurrentStopLoss, item.OriginalTakeProfit, item.FinalTakeProfit ?? item.CurrentTakeProfit, item.MarginUsedUsdt, item.Leverage, item.EntryNotionalUsdt, item.Quantity, item.GrossPnlUsdt, item.TotalFeesUsdt, item.NetPnlUsdt, item.NetPnlPercent, item.MarginUsedUsdt is > 0 ? item.NetPnlUsdt / item.MarginUsedUsdt * 100 : null, risk == 0 ? null : item.NetPnlUsdt / risk, item.ExitReason?.ToString(), item.SignalOpen, item.SignalClose, item.SignalEma9, item.SignalEma15, item.SignalEma100, item.SignalGapPercent, item.IsReentry, item.StopSourceType, item.StopSourceTimeUtc, events, session.Id, session.RiskReward, session.WaitForConfirmationCandle, session.UseEma100Filter, session.MinEmaGapPercent, session.MaxStopDistancePercent, session.PositionSizingMode.ToString(), session.StartingBalanceUsdt, session.MarginPerTradePercent, session.FeePercentPerSide, null, null, null, null); }
}

public static class SimpleExports
{
    private static readonly string[] Headers = ["Source", "Market Data Source", "Account Currency", "Symbol", "Interval", "Direction", "Status", "Entry time", "Exit time", "Entry", "Exit", "Initial SL", "Final SL", "Original TP", "Final TP", "Lots", "Required Margin", "Margin Used", "Entry Bid", "Entry Ask", "Entry Spread", "Exit Bid", "Exit Ask", "Exit Spread", "Gross P/L", "Trading Costs", "Net P/L", "Net P/L %", "P/L % Basis", "R", "Exit Reason", "Signal Open", "Signal Close", "EMA9", "EMA15", "EMA100", "EMA Gap", "Is Re-entry"];
    public static byte[] Xlsx(IReadOnlyList<TradeExportRow> rows)
    {
        using var stream = new MemoryStream(); using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Write(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            Write(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Write(zip, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Trades\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Write(zip, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            var sheet = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>"); AppendRow(sheet, Headers); foreach (var row in rows) AppendRow(sheet, Values(row)); sheet.Append("</sheetData></worksheet>"); Write(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return stream.ToArray();
    }
    public static byte[] Pdf(TradeExportRow row)
    {
        var candleDirection = row.SignalOpen is null ? "Legacy" : row.SignalClose > row.SignalOpen ? "Bullish" : row.SignalClose < row.SignalOpen ? "Bearish" : "Doji";
        var mt5 = row.MarketDataSource == MarketDataSource.Mt5Exness;
        var lines = new List<string> { "EMA-Bot Trade Analysis", $"Source: {(mt5 ? "Paper / MT5 Exness" : row.Source)}; Trade ID: {row.Id}; Parent: {row.ParentId}", $"Symbol/timeframe: {row.Symbol} {row.Interval}; Direction: {row.Direction}; Re-entry: {row.IsReentry}", $"Settings: R:R {row.RiskReward}; confirmation {row.WaitForConfirmation}; EMA100 {row.UseEma100}", $"Minimum EMA gap: {row.MinEmaGap}%; maximum stop distance: {row.MaxStopDistance}%", mt5 ? $"Sizing: {row.PositionSizingMode}; currency {row.AccountCurrency}; lots {row.Lots}; required margin {row.RequiredMargin}; entry equity {row.AccountEquityAtEntry}" : $"Sizing: {row.PositionSizingMode}; start balance {row.StartingBalance}; margin {row.MarginPercent}%; leverage {row.Leverage}; fee {row.FeePercent}%", $"Crossover: {row.CrossoverTime:O}; signal: {row.SignalTime:O}; signal candle {row.SignalOpen}/{row.SignalClose} {candleDirection}", $"Entry/exit: {row.EntryTime:O} / {row.ExitTime:O}; {row.Entry} / {row.Exit}", mt5 ? $"Entry Bid/Ask/spread: {row.EntryBid}/{row.EntryAsk}/{row.EntrySpread}; Exit Bid/Ask/spread: {row.ExitBid}/{row.ExitAsk}/{row.ExitSpread}" : $"Margin: {row.Margin}; Notional: {row.Notional}; Quantity: {row.Quantity}", $"SL: {row.InitialStop} / {row.FinalStop}; source {row.StopSource} at {row.StopSourceTime:O}; TP: {row.OriginalTarget} / {row.FinalTarget}", $"Result: {row.ExitReason}; gross {row.GrossPnl} {row.AccountCurrency}; trading costs {row.Fees} {row.AccountCurrency}; net {row.NetPnl} {row.AccountCurrency}; net % {row.NetPercent}; basis {row.PnlPercentBasis}; R {row.R}", $"EMA9/15/100: {row.Ema9}/{row.Ema15}/{row.Ema100}; gap {row.GapPercent}%", "Management timeline:" };
        lines.AddRange(row.ManagementTimeline.DefaultIfEmpty("No management events were recorded."));
        lines.Add(row.IsReentry ? "A stopped trade kept its EMA regime; a new directional continuation candle created the single allowed re-entry." : mt5 ? "The signal scheduled entry for the exact next bar. Paper entered on the first executable Ask quote for Long or Bid quote for Short observed on that bar." : "EMA crossover strategy entry occurred at the following candle open.");
        lines.Add("Liquidation and maintenance-margin tiers are not modeled.");
        var content = new StringBuilder("BT /F1 11 Tf 50 760 Td "); foreach (var line in lines) content.Append('(').Append(line.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)")).Append(") Tj 0 -20 Td "); content.Append("ET");
        var objects = new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>", $"<< /Length {Encoding.ASCII.GetByteCount(content.ToString())} >>\nstream\n{content}\nendstream", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>" };
        using var stream = new MemoryStream(); WriteAscii(stream, "%PDF-1.4\n"); var offsets = new List<long> { 0 }; for (var i = 0; i < objects.Length; i++) { offsets.Add(stream.Position); WriteAscii(stream, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); } var start = stream.Position; WriteAscii(stream, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n"); for (var i = 1; i < offsets.Count; i++) WriteAscii(stream, $"{offsets[i]:D10} 00000 n \n"); WriteAscii(stream, $"trailer << /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{start}\n%%EOF"); return stream.ToArray();
    }
    private static IEnumerable<string?> Values(TradeExportRow row) => [row.Source, row.MarketDataSource.ToString(), row.AccountCurrency, row.Symbol, row.Interval, row.Direction, row.Status, row.EntryTime.ToString("O"), row.ExitTime?.ToString("O"), row.Entry.ToString(CultureInfo.InvariantCulture), row.Exit?.ToString(CultureInfo.InvariantCulture), row.InitialStop.ToString(CultureInfo.InvariantCulture), row.FinalStop.ToString(CultureInfo.InvariantCulture), row.OriginalTarget.ToString(CultureInfo.InvariantCulture), row.FinalTarget.ToString(CultureInfo.InvariantCulture), row.Lots?.ToString(CultureInfo.InvariantCulture), row.RequiredMargin?.ToString(CultureInfo.InvariantCulture), row.MarginUsed?.ToString(CultureInfo.InvariantCulture), row.EntryBid?.ToString(CultureInfo.InvariantCulture), row.EntryAsk?.ToString(CultureInfo.InvariantCulture), row.EntrySpread?.ToString(CultureInfo.InvariantCulture), row.ExitBid?.ToString(CultureInfo.InvariantCulture), row.ExitAsk?.ToString(CultureInfo.InvariantCulture), row.ExitSpread?.ToString(CultureInfo.InvariantCulture), row.GrossPnl.ToString(CultureInfo.InvariantCulture), row.Fees.ToString(CultureInfo.InvariantCulture), row.NetPnl.ToString(CultureInfo.InvariantCulture), row.NetPercent.ToString(CultureInfo.InvariantCulture), row.PnlPercentBasis, row.R?.ToString(CultureInfo.InvariantCulture), row.ExitReason, row.SignalOpen?.ToString(CultureInfo.InvariantCulture), row.SignalClose.ToString(CultureInfo.InvariantCulture), row.Ema9?.ToString(CultureInfo.InvariantCulture), row.Ema15?.ToString(CultureInfo.InvariantCulture), row.Ema100?.ToString(CultureInfo.InvariantCulture), row.GapPercent?.ToString(CultureInfo.InvariantCulture), row.IsReentry.ToString()];
    private static void AppendRow(StringBuilder sheet, IEnumerable<string?> values) { sheet.Append("<row>"); foreach (var value in values) sheet.Append("<c t=\"inlineStr\"><is><t>").Append(SecurityElement.Escape(value ?? string.Empty)).Append("</t></is></c>"); sheet.Append("</row>"); }
    private static void Write(ZipArchive archive, string path, string text) { using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8); writer.Write(text); }
    private static void WriteAscii(Stream stream, string text) { var bytes = Encoding.ASCII.GetBytes(text); stream.Write(bytes, 0, bytes.Length); }
}
