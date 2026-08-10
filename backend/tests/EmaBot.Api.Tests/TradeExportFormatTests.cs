using System.IO.Compression;
using System.Text;
using EmaBot.Api.Controllers;
using EmaBot.Api.Models;

namespace EmaBot.Api.Tests;

public sealed class TradeExportFormatTests
{
    [Fact]
    public void Xlsx_IsOpenXmlZipWithHeadersAndRows()
    {
        var bytes = SimpleExports.Xlsx([Row()]);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("xl/workbook.xml"));
        var sheet = archive.GetEntry("xl/worksheets/sheet1.xml"); Assert.NotNull(sheet);
        using var reader = new StreamReader(sheet!.Open()); var xml = reader.ReadToEnd();
        Assert.Contains("Source", xml); Assert.Contains("BTCUSDT", xml); Assert.Contains("Backtest", xml);
    }

    [Fact]
    public void Pdf_HasValidHeaderFooterAndXrefOffsets()
    {
        var bytes = SimpleExports.Pdf(Row()); var text = Encoding.ASCII.GetString(bytes);
        Assert.StartsWith("%PDF-", text); Assert.EndsWith("%%EOF", text);
        var xref = text.IndexOf("xref\n", StringComparison.Ordinal); Assert.True(xref > 0);
        var lines = text[xref..].Split('\n');
        for (var index = 3; index <= 7; index++) { var offset = int.Parse(lines[index][..10]); Assert.Equal($"{index - 2} 0 obj", text.Substring(offset, 7)); }
    }

    private static TradeExportRow Row() => new(42, "Backtest", "BTCUSDT", "3m", "Long", "Closed", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(2), DateTimeOffset.UnixEpoch.AddMinutes(3), DateTimeOffset.UnixEpoch.AddMinutes(6), 100m, 102m, 99m, 99m, 102m, 102m, 10m, 5m, 500m, 5m, 10m, 1m, 9m, 1.8m, 90m, 2m, "TakeProfit", 99m, 100m, 101m, 99m, 98m, 2m, true, StopSourceType.Pivot, DateTimeOffset.UnixEpoch, ["1970-01-01T00:03:00.0000000+00:00 Entry; price 100"], 7, 2m, true, true, 0.5m, 3m, "FixedNotional", 1000m, 1m, 0.04m);
}
