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
        var rows = await RowsAsync(source, symbol, interval, direction, outcome, token);
        return File(SimpleExports.Xlsx(rows), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ema-bot-trades.xlsx");
    }

    [HttpGet("{source}/{id:int}/pdf")]
    public async Task<IActionResult> Pdf(string source, int id, CancellationToken token)
    {
        var row = (await RowsAsync(source, null, null, null, null, token)).SingleOrDefault(item => item.Id == id && string.Equals(item.Source, source, StringComparison.OrdinalIgnoreCase));
        return row is null ? NotFound(new ApiMessage("Trade not found.")) : File(SimpleExports.Pdf(row), "application/pdf", $"ema-bot-trade-{row.Source.ToLowerInvariant()}-{id}.pdf");
    }

    private async Task<List<TradeExportRow>> RowsAsync(string? source, string? symbol, string? interval, string? direction, string? outcome, CancellationToken token)
    {
        var rows = new List<TradeExportRow>(); var requested = source?.Trim(); var normalized = string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(requested) || string.Equals(requested, "Backtest", StringComparison.OrdinalIgnoreCase))
        {
            var query = database.BacktestTrades.AsNoTracking().Include(item => item.BacktestRun).AsQueryable();
            if (normalized is not null) query = query.Where(item => item.BacktestRun!.Symbol == normalized);
            if (!string.IsNullOrWhiteSpace(interval)) query = query.Where(item => item.BacktestRun!.Interval == interval);
            if (!string.IsNullOrWhiteSpace(direction) && Enum.TryParse<SignalDirection>(direction, true, out var parsed)) query = query.Where(item => item.Direction == parsed);
            rows.AddRange((await query.ToListAsync(token)).Select(item => TradeExportRow.From(item)));
        }
        if (string.IsNullOrWhiteSpace(requested) || string.Equals(requested, "Paper", StringComparison.OrdinalIgnoreCase))
        {
            var query = database.PaperTrades.AsNoTracking().Include(item => item.PaperSession).AsQueryable();
            if (normalized is not null) query = query.Where(item => item.Symbol == normalized);
            if (!string.IsNullOrWhiteSpace(interval)) query = query.Where(item => item.Interval == interval);
            if (!string.IsNullOrWhiteSpace(direction) && Enum.TryParse<SignalDirection>(direction, true, out var parsed)) query = query.Where(item => item.Direction == parsed);
            rows.AddRange((await query.ToListAsync(token)).Select(item => TradeExportRow.From(item)));
        }
        return rows.Where(item => string.IsNullOrWhiteSpace(outcome) || outcome.Equals("All", StringComparison.OrdinalIgnoreCase) || outcome.Equals("Open", StringComparison.OrdinalIgnoreCase) && item.Status == "Open" || outcome.Equals("Win", StringComparison.OrdinalIgnoreCase) && item.NetPnl > 0 || outcome.Equals("Loss", StringComparison.OrdinalIgnoreCase) && item.NetPnl < 0 || outcome is "BreakEven" or "Break-even" && item.Status == "Closed" && item.NetPnl == 0).OrderByDescending(item => item.EntryTime).ToList();
    }
}

internal sealed record TradeExportRow(int Id, string Source, string Symbol, string Interval, string Direction, string Status, DateTimeOffset EntryTime, DateTimeOffset? ExitTime, decimal Entry, decimal? Exit, decimal InitialStop, decimal FinalStop, decimal OriginalTarget, decimal FinalTarget, decimal? Margin, decimal? Leverage, decimal Notional, decimal Quantity, decimal GrossPnl, decimal Fees, decimal NetPnl, decimal NetPercent, decimal? MarginReturnPercent, decimal? R, string? ExitReason, decimal? SignalOpen, decimal SignalClose, decimal? Ema9, decimal? Ema15, decimal? Ema100, decimal? GapPercent, bool IsReentry)
{
    public static TradeExportRow From(BacktestTrade item) => new(item.Id, "Backtest", item.BacktestRun!.Symbol, item.BacktestRun.Interval, item.Direction.ToString(), "Closed", item.EntryTimeUtc, item.ExitTimeUtc, item.EntryPrice, item.ExitPrice, item.InitialStopLoss, item.FinalStopLoss, item.OriginalTakeProfit, item.FinalTakeProfit, item.MarginUsedUsdt, item.Leverage, item.EntryNotionalUsdt, item.Quantity, item.GrossPnlUsdt, item.TotalFeesUsdt, item.NetPnlUsdt, item.NetPnlPercent, item.MarginUsedUsdt is > 0 ? item.NetPnlUsdt / item.MarginUsedUsdt * 100 : null, item.NetRMultiple, item.ExitReason.ToString(), item.SignalOpen, item.SignalClose, item.SignalEma9, item.SignalEma15, item.SignalEma100, item.SignalGapPercent, item.IsReentry);
    public static TradeExportRow From(PaperTrade item) { var risk = Math.Abs(item.EntryPrice - item.InitialStopLoss) * item.Quantity; return new(item.Id, "Paper", item.Symbol, item.Interval, item.Direction.ToString(), item.Status.ToString(), item.EntryTimeUtc, item.ExitTimeUtc, item.EntryPrice, item.ExitPrice, item.InitialStopLoss, item.FinalStopLoss ?? item.CurrentStopLoss, item.OriginalTakeProfit, item.FinalTakeProfit ?? item.CurrentTakeProfit, item.MarginUsedUsdt, item.Leverage, item.EntryNotionalUsdt, item.Quantity, item.GrossPnlUsdt, item.TotalFeesUsdt, item.NetPnlUsdt, item.NetPnlPercent, item.MarginUsedUsdt is > 0 ? item.NetPnlUsdt / item.MarginUsedUsdt * 100 : null, risk == 0 ? null : item.NetPnlUsdt / risk, item.ExitReason?.ToString(), item.SignalOpen, item.SignalClose, item.SignalEma9, item.SignalEma15, item.SignalEma100, item.SignalGapPercent, item.IsReentry); }
}

internal static class SimpleExports
{
    private static readonly string[] Headers = ["Source", "Symbol", "Interval", "Direction", "Status", "Entry time", "Exit time", "Entry", "Exit", "Initial SL", "Final SL", "Original TP", "Final TP", "Margin", "Leverage", "Notional", "Quantity", "Gross PnL", "Fees", "Net PnL", "Net %", "Margin Return %", "R", "Exit Reason", "Signal Open", "Signal Close", "EMA9", "EMA15", "EMA100", "EMA Gap", "Is Re-entry"];
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
        var lines = new[] { "EMA-Bot Trade Analysis", $"{row.Source} {row.Symbol} {row.Interval} {row.Direction}", $"Entry: {row.Entry}  Exit: {row.Exit}  Status: {row.Status}", $"SL: {row.InitialStop} / {row.FinalStop}  TP: {row.OriginalTarget} / {row.FinalTarget}", $"Margin: {row.Margin}  Leverage: {row.Leverage}  Notional: {row.Notional}  Quantity: {row.Quantity}", $"Gross: {row.GrossPnl}  Fees: {row.Fees}  Net: {row.NetPnl}  Margin return: {row.MarginReturnPercent}%", $"Signal open/close: {row.SignalOpen} / {row.SignalClose}; EMA gap: {row.GapPercent}%", $"Re-entry: {row.IsReentry}. Liquidation and maintenance-margin tiers are not modeled." };
        var content = new StringBuilder("BT /F1 11 Tf 50 760 Td "); foreach (var line in lines) content.Append('(').Append(line.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)")).Append(") Tj 0 -20 Td "); content.Append("ET");
        var objects = new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>", $"<< /Length {Encoding.ASCII.GetByteCount(content.ToString())} >>\nstream\n{content}\nendstream", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>" };
        using var stream = new MemoryStream(); using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true); writer.Write("%PDF-1.4\n"); var offsets = new List<long> { 0 }; for (var i = 0; i < objects.Length; i++) { offsets.Add(stream.Position); writer.Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); } writer.Flush(); var start = stream.Position; writer.Write($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n"); for (var i = 1; i < offsets.Count; i++) writer.Write($"{offsets[i]:D10} 00000 n \n"); writer.Write($"trailer << /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{start}\n%%EOF"); writer.Flush(); return stream.ToArray();
    }
    private static IEnumerable<string?> Values(TradeExportRow row) => [row.Source, row.Symbol, row.Interval, row.Direction, row.Status, row.EntryTime.ToString("O"), row.ExitTime?.ToString("O"), row.Entry.ToString(CultureInfo.InvariantCulture), row.Exit?.ToString(CultureInfo.InvariantCulture), row.InitialStop.ToString(CultureInfo.InvariantCulture), row.FinalStop.ToString(CultureInfo.InvariantCulture), row.OriginalTarget.ToString(CultureInfo.InvariantCulture), row.FinalTarget.ToString(CultureInfo.InvariantCulture), row.Margin?.ToString(CultureInfo.InvariantCulture), row.Leverage?.ToString(CultureInfo.InvariantCulture), row.Notional.ToString(CultureInfo.InvariantCulture), row.Quantity.ToString(CultureInfo.InvariantCulture), row.GrossPnl.ToString(CultureInfo.InvariantCulture), row.Fees.ToString(CultureInfo.InvariantCulture), row.NetPnl.ToString(CultureInfo.InvariantCulture), row.NetPercent.ToString(CultureInfo.InvariantCulture), row.MarginReturnPercent?.ToString(CultureInfo.InvariantCulture), row.R?.ToString(CultureInfo.InvariantCulture), row.ExitReason, row.SignalOpen?.ToString(CultureInfo.InvariantCulture), row.SignalClose.ToString(CultureInfo.InvariantCulture), row.Ema9?.ToString(CultureInfo.InvariantCulture), row.Ema15?.ToString(CultureInfo.InvariantCulture), row.Ema100?.ToString(CultureInfo.InvariantCulture), row.GapPercent?.ToString(CultureInfo.InvariantCulture), row.IsReentry.ToString()];
    private static void AppendRow(StringBuilder sheet, IEnumerable<string?> values) { sheet.Append("<row>"); foreach (var value in values) sheet.Append("<c t=\"inlineStr\"><is><t>").Append(SecurityElement.Escape(value ?? string.Empty)).Append("</t></is></c>"); sheet.Append("</row>"); }
    private static void Write(ZipArchive archive, string path, string text) { using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8); writer.Write(text); }
}
