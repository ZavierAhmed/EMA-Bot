using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace EmaBot.Api.Binance;

public sealed record BinanceKlineUpdate(string Symbol, string Interval, DateTimeOffset EventTimeUtc, DateTimeOffset OpenTimeUtc, DateTimeOffset CloseTimeUtc, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume, bool IsClosed);

public interface IBinanceFuturesStreamClient
{
    Task StreamAsync(IReadOnlyCollection<string> symbols, string interval, Func<BinanceKlineUpdate, CancellationToken, Task> onUpdate, Action<string>? onStateChange, CancellationToken cancellationToken);
}

public sealed class BinanceFuturesStreamClient(ILogger<BinanceFuturesStreamClient> logger) : IBinanceFuturesStreamClient
{
    private static readonly Uri Endpoint = new("wss://fstream.binance.com/market/stream");
    private static readonly TimeSpan[] ReconnectDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    public async Task StreamAsync(IReadOnlyCollection<string> symbols, string interval, Func<BinanceKlineUpdate, CancellationToken, Task> onUpdate, Action<string>? onStateChange, CancellationToken cancellationToken)
    {
        if (symbols.Count == 0 || !BinanceIntervals.IsSupported(interval)) throw new ArgumentException("A supported interval and at least one symbol are required.");
        var streams = symbols.Select(symbol => $"{symbol.ToLowerInvariant()}@kline_{interval}").ToArray();
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                onStateChange?.Invoke("Connecting");
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(Endpoint, cancellationToken);
                var subscription = JsonSerializer.Serialize(new { method = "SUBSCRIBE", @params = streams, id = Guid.NewGuid().ToString("N") });
                await socket.SendAsync(Encoding.UTF8.GetBytes(subscription), WebSocketMessageType.Text, true, cancellationToken);
                onStateChange?.Invoke("Connected"); attempt = 0;
                var buffer = new byte[16 * 1024];
                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    using var message = new MemoryStream(); WebSocketReceiveResult result;
                    do { result = await socket.ReceiveAsync(buffer, cancellationToken); if (result.MessageType == WebSocketMessageType.Close) break; message.Write(buffer, 0, result.Count); } while (!result.EndOfMessage);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    if (result.MessageType != WebSocketMessageType.Text) continue;
                    if (BinanceKlineParser.TryParse(Encoding.UTF8.GetString(message.ToArray()), out var update)) await onUpdate(update, cancellationToken);
                    else logger.LogWarning("Skipped malformed Binance kline stream message.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Binance stream connection ended unexpectedly.");
            }
            if (cancellationToken.IsCancellationRequested) break;
            var delay = ReconnectDelays[Math.Min(attempt++, ReconnectDelays.Length - 1)];
            onStateChange?.Invoke("Reconnecting");
            await Task.Delay(delay, cancellationToken);
        }
        onStateChange?.Invoke("Disconnected");
    }
}

public static class BinanceKlineParser
{
    public static bool TryParse(string json, out BinanceKlineUpdate update)
    {
        update = default!;
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            if (root.TryGetProperty("data", out var combined)) root = combined;
            if (!root.TryGetProperty("e", out var eventType) || eventType.GetString() != "kline" || !root.TryGetProperty("k", out var kline)) return false;
            var symbol = String(root, "s"); var interval = String(kline, "i");
            if (!BinanceIntervals.IsSupported(interval)) return false;
            update = new BinanceKlineUpdate(symbol.ToUpperInvariant(), interval, Timestamp(root, "E"), Timestamp(kline, "t"), Timestamp(kline, "T"), Decimal(kline, "o"), Decimal(kline, "h"), Decimal(kline, "l"), Decimal(kline, "c"), Decimal(kline, "v"), kline.GetProperty("x").GetBoolean());
            return true;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or ArgumentOutOfRangeException) { return false; }
    }

    private static string String(JsonElement element, string property) => element.GetProperty(property).GetString() is { Length: > 0 } value ? value : throw new FormatException();
    private static DateTimeOffset Timestamp(JsonElement element, string property) => DateTimeOffset.FromUnixTimeMilliseconds(element.GetProperty(property).GetInt64());
    private static decimal Decimal(JsonElement element, string property) => decimal.Parse(String(element, property), NumberStyles.Number, CultureInfo.InvariantCulture);
}
