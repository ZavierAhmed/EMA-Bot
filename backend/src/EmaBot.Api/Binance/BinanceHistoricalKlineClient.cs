using System.Globalization;
using System.Text.Json;
using EmaBot.Api.Market;

namespace EmaBot.Api.Binance;

/// <summary>Raw legacy Binance client retained only for historical research klines.</summary>
public sealed class BinanceHistoricalKlineClient(HttpClient httpClient, TimeProvider timeProvider) : IBinanceHistoricalKlineClient
{
    public async Task<IReadOnlyList<Candle>> GetKlinesAsync(string symbol, string interval, DateTimeOffset? startTimeUtc, DateTimeOffset? endTimeUtc, int? limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbol) || !symbol.All(char.IsLetterOrDigit)) throw new ArgumentException("A valid instrument symbol is required.", nameof(symbol));
        if (!StrategyTimeframes.IsSupported(interval)) throw new ArgumentException("Unsupported timeframe.", nameof(interval));
        var requestedLimit = limit ?? 300;
        if (requestedLimit is < 1 or > 1500) throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 1500.");
        if (startTimeUtc > endTimeUtc) throw new ArgumentException("Start time must not be after end time.");

        var parameters = new List<string> { $"symbol={Uri.EscapeDataString(symbol.ToUpperInvariant())}", $"interval={Uri.EscapeDataString(interval)}", $"limit={requestedLimit}" };
        if (startTimeUtc.HasValue) parameters.Add($"startTime={startTimeUtc.Value.ToUnixTimeMilliseconds()}");
        if (endTimeUtc.HasValue) parameters.Add($"endTime={endTimeUtc.Value.ToUnixTimeMilliseconds()}");
        using var document = await GetJsonAsync($"fapi/v1/klines?{string.Join('&', parameters)}", cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new BinanceApiException("Binance returned malformed kline data.");

        var now = timeProvider.GetUtcNow();
        var candles = new List<Candle>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 7) throw new BinanceApiException("Binance returned a malformed kline entry.");
            try
            {
                var openTime = DateTimeOffset.FromUnixTimeMilliseconds(item[0].GetInt64());
                var closeTime = DateTimeOffset.FromUnixTimeMilliseconds(item[6].GetInt64());
                candles.Add(new Candle(openTime, closeTime, ParseDecimal(item[1]), ParseDecimal(item[2]), ParseDecimal(item[3]), ParseDecimal(item[4]), ParseDecimal(item[5]), closeTime <= now));
            }
            catch (Exception exception) when (exception is FormatException or InvalidOperationException or ArgumentOutOfRangeException)
            {
                throw new BinanceApiException("Binance returned a malformed kline value.");
            }
        }
        return candles;
    }

    private async Task<JsonDocument> GetJsonAsync(string relativeUri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(relativeUri, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var message = "Binance market data is currently unavailable.";
                try { using var error = JsonDocument.Parse(body); if (error.RootElement.TryGetProperty("msg", out var value) && value.ValueKind == JsonValueKind.String) message = value.GetString() ?? message; } catch (JsonException) { }
                throw new BinanceApiException(message, (int)response.StatusCode);
            }
            try { return JsonDocument.Parse(body); }
            catch (JsonException) { throw new BinanceApiException("Binance returned malformed JSON."); }
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new BinanceApiException("Binance market data request timed out.", StatusCodes.Status504GatewayTimeout); }
        catch (TimeoutException) when (!cancellationToken.IsCancellationRequested) { throw new BinanceApiException("Binance market data request timed out.", StatusCodes.Status504GatewayTimeout); }
    }

    private static decimal ParseDecimal(JsonElement element) => decimal.Parse(element.GetString() ?? throw new FormatException(), NumberStyles.Number, CultureInfo.InvariantCulture);
}
