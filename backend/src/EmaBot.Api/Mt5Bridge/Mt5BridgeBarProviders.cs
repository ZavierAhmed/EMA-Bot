using EmaBot.Api.Market;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Mt5Bridge;

public sealed class Mt5BridgeHistoricalMarketDataProvider(IMt5BridgeRequestClient bridge) : IHistoricalMarketDataProvider
{
    public const int MaximumCandles = 200_000;
    private const int PageBars = 1_000;

    public async Task<IReadOnlyList<Candle>> GetLatestAsync(string symbol, string timeframe, int count, CancellationToken cancellationToken)
    {
        ValidateTimeframe(timeframe);
        if (count is < 1 or > 1_500) throw new ArgumentOutOfRangeException(nameof(count), "Count must be between 1 and 1500.");
        var bars = await RequestBarsAsync(Mt5BridgeOperation.GetLatestBars, new Mt5GetLatestBarsRequest(symbol, timeframe, count), cancellationToken);
        return MapClosed(bars).TakeLast(count).ToArray();
    }

    public async Task<IReadOnlyList<Candle>> GetRangeAsync(string symbol, string timeframe, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken)
    {
        ValidateTimeframe(timeframe);
        if (startUtc >= endUtc) throw new ArgumentException("Start UTC must be before end UTC.");
        var all = new SortedDictionary<DateTimeOffset, Mt5BarPayload>();
        var cursor = startUtc;
        var window = TimeframeSpan(timeframe) * PageBars;
        while (cursor < endUtc)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidateEnd = cursor + window;
            var windowEnd = candidateEnd < endUtc ? candidateEnd : endUtc;
            var page = await RequestBarsAsync(Mt5BridgeOperation.GetBarsRange, new Mt5GetBarsRangeRequest(symbol, timeframe, cursor.ToUnixTimeSeconds(), ToInclusiveStopUnixSeconds(windowEnd)), cancellationToken);
            foreach (var bar in page) all[bar.OpenTimeUtc] = bar;
            if (all.Count > MaximumCandles) throw new ArgumentException($"Backtests cannot exceed {MaximumCandles:N0} candles.");
            if (windowEnd == endUtc) break;
            cursor = windowEnd;
        }
        return MapClosed(all.Values).Where(candle => candle.OpenTimeUtc >= startUtc && candle.CloseTimeUtc <= endUtc).ToArray();
    }

    internal static IReadOnlyList<Candle> MapClosed(IEnumerable<Mt5BarPayload> source)
    {
        var bars = source.GroupBy(bar => bar.OpenTimeUtc).Select(group => group.Last()).OrderBy(bar => bar.OpenTimeUtc).ToArray();
        var candles = new List<Candle>();
        for (var index = 0; index < bars.Length - 1; index++)
        {
            var bar = bars[index]; var successor = bars[index + 1];
            if (bar.IsCurrent || successor.OpenTimeUtc <= bar.OpenTimeUtc) continue;
            candles.Add(new Candle(bar.OpenTimeUtc, successor.OpenTimeUtc.AddMilliseconds(-1), bar.Open, bar.High, bar.Low, bar.Close, bar.RealVolume > 0 ? bar.RealVolume : bar.TickVolume, true));
        }
        return candles;
    }

    internal static void ValidateTimeframe(string timeframe)
    {
        if (!Mt5NativeTimeframes.IsSupported(timeframe)) throw new ArgumentException("The 3d timeframe is supported by EMA-Bot but is not native to the current MT5 market-data adapter.", nameof(timeframe));
    }

    internal static long ToInclusiveStopUnixSeconds(DateTimeOffset value)
    {
        var seconds = value.ToUnixTimeSeconds();
        return value.Ticks % TimeSpan.TicksPerSecond == 0 ? seconds : checked(seconds + 1);
    }

    private async Task<IReadOnlyList<Mt5BarPayload>> RequestBarsAsync(Mt5BridgeOperation operation, object request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await bridge.SendAsync(operation, request, cancellationToken);
            return response.DeserializePayload<IReadOnlyList<Mt5BarPayload>>() ?? throw new MarketDataProviderException("MT5 historical bars", MarketDataErrorKind.InvalidResponse, "MT5 returned invalid bar data.");
        }
        catch (Exception exception) { throw Translate(exception); }
    }

    private static TimeSpan TimeframeSpan(string timeframe) => timeframe switch { "3m" => TimeSpan.FromMinutes(3), "5m" => TimeSpan.FromMinutes(5), "15m" => TimeSpan.FromMinutes(15), "30m" => TimeSpan.FromMinutes(30), "1h" => TimeSpan.FromHours(1), "2h" => TimeSpan.FromHours(2), "4h" => TimeSpan.FromHours(4), "6h" => TimeSpan.FromHours(6), "8h" => TimeSpan.FromHours(8), "12h" => TimeSpan.FromHours(12), "1d" => TimeSpan.FromDays(1), "1w" => TimeSpan.FromDays(7), "1M" => TimeSpan.FromDays(31), _ => throw new ArgumentException("Unsupported MT5 timeframe.") };
    internal static MarketDataProviderException Translate(Exception exception) => exception switch
    {
        MarketDataProviderException market => market,
        Mt5BridgeUnavailableException or Mt5BridgeDisconnectedException => new("MT5 historical bars", MarketDataErrorKind.Unavailable, "The MT5 bridge is not connected.", exception),
        Mt5BridgeRequestTimeoutException => new("MT5 historical bars", MarketDataErrorKind.Timeout, "The MT5 bar request timed out.", exception),
        Mt5BridgeRemoteException remote when remote.Code is "HistoryNotReady" or "NotFound" or "SymbolUnavailable" => new("MT5 historical bars", MarketDataErrorKind.Unavailable, "MT5 history is not available yet.", exception),
        Mt5BridgeRemoteException remote => new("MT5 historical bars", MarketDataErrorKind.InvalidResponse, remote.Message, exception),
        _ => new("MT5 historical bars", MarketDataErrorKind.InvalidResponse, "MT5 returned invalid bar data.", exception)
    };
}

public sealed class Mt5BridgeMarketBarStreamProvider(IMt5BridgeRequestClient bridge, IOptions<Mt5MarketDataOptions> options) : IMarketBarStreamProvider
{
    private const int MaximumSymbols = 100;
    public bool IsConfigured => bridge.IsConnected;

    public async Task StreamAsync(IReadOnlyCollection<string> symbols, string timeframe, Func<MarketBarUpdate, CancellationToken, Task> onUpdate, Action<string>? onStateChange, CancellationToken cancellationToken)
    {
        Mt5BridgeHistoricalMarketDataProvider.ValidateTimeframe(timeframe);
        if (symbols.Count is 0 or > MaximumSymbols) throw new ArgumentException($"MT5 live bar streaming requires between 1 and {MaximumSymbols} symbols.");
        if (!bridge.IsConnected) throw new MarketDataProviderException("MT5 live bars", MarketDataErrorKind.Unavailable, "The MT5 bridge is not connected.");
        onStateChange?.Invoke("Connecting");
        var state = symbols.ToDictionary(symbol => symbol, _ => new StreamState(), StringComparer.Ordinal);
        onStateChange?.Invoke("Connected");
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var snapshots = await Task.WhenAll(symbols.Select(symbol => SnapshotAsync(symbol, timeframe, cancellationToken)));
                foreach (var snapshot in snapshots) await EmitAsync(snapshot, state[snapshot.BrokerSymbol], onUpdate, cancellationToken);
            }
            catch (MarketDataProviderException) { onStateChange?.Invoke("Degraded"); throw; }
            await Task.Delay(options.Value.PollMilliseconds, cancellationToken);
        }
    }

    internal static async Task EmitAsync(Mt5BarSnapshotPayload snapshot, StreamState state, Func<MarketBarUpdate, CancellationToken, Task> onUpdate, CancellationToken cancellationToken)
    {
        if (!state.Initialized)
        {
            state.Initialized = true; state.CurrentOpen = snapshot.Current.OpenTimeUtc; state.PreviousOpen = snapshot.PreviousClosed.OpenTimeUtc; state.LastEvent = snapshot.EventTimeUtc;
            await onUpdate(ToUpdate(snapshot.Current, snapshot.EventTimeUtc, false, snapshot.Current.OpenTimeUtc), cancellationToken); return;
        }
        if (snapshot.Current.OpenTimeUtc != state.CurrentOpen)
        {
            if (snapshot.PreviousClosed.OpenTimeUtc != state.LastClosed) await onUpdate(ToUpdate(snapshot.PreviousClosed, snapshot.EventTimeUtc, true, snapshot.Current.OpenTimeUtc), cancellationToken);
            state.LastClosed = snapshot.PreviousClosed.OpenTimeUtc; state.PreviousOpen = snapshot.PreviousClosed.OpenTimeUtc; state.CurrentOpen = snapshot.Current.OpenTimeUtc; state.LastEvent = snapshot.EventTimeUtc;
            await onUpdate(ToUpdate(snapshot.Current, snapshot.EventTimeUtc, false, snapshot.Current.OpenTimeUtc), cancellationToken); return;
        }
        if (snapshot.EventTimeUtc > state.LastEvent)
        {
            state.LastEvent = snapshot.EventTimeUtc;
            await onUpdate(ToUpdate(snapshot.Current, snapshot.EventTimeUtc, false, snapshot.Current.OpenTimeUtc), cancellationToken);
        }
    }

    private async Task<Mt5BarSnapshotPayload> SnapshotAsync(string symbol, string timeframe, CancellationToken cancellationToken)
    {
        try
        {
            var response = await bridge.SendAsync(Mt5BridgeOperation.GetBarSnapshot, new Mt5GetBarSnapshotRequest(symbol, timeframe), cancellationToken);
            return response.DeserializePayload<Mt5BarSnapshotPayload>() ?? throw new MarketDataProviderException("MT5 live bars", MarketDataErrorKind.InvalidResponse, "MT5 returned an invalid bar snapshot.");
        }
        catch (Exception exception) { throw Mt5BridgeHistoricalMarketDataProvider.Translate(exception); }
    }

    private static MarketBarUpdate ToUpdate(Mt5BarPayload bar, DateTimeOffset eventTime, bool closed, DateTimeOffset successorOpen) => new(bar.BrokerSymbol, bar.Timeframe, eventTime, bar.OpenTimeUtc, closed ? successorOpen.AddMilliseconds(-1) : bar.OpenTimeUtc, bar.Open, bar.High, bar.Low, bar.Close, bar.RealVolume > 0 ? bar.RealVolume : bar.TickVolume, closed);
    internal sealed class StreamState { public bool Initialized; public DateTimeOffset CurrentOpen; public DateTimeOffset PreviousOpen; public DateTimeOffset LastClosed; public DateTimeOffset LastEvent; }
}
