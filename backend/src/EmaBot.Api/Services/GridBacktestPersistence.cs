using EmaBot.Api.Models;
using EmaBot.Api.Strategy.Grid;

namespace EmaBot.Api.Services;

public static class GridBacktestPersistence
{
    public static GridBacktestRun Map(GridHistoricalBacktestResult result, DateTimeOffset createdAt)
    {
        var request = result.Request; var spec = result.Instrument.Spec; var d = result.Diagnostics;
        var settings = request.Settings ?? GridHistoricalStrategyProfile.Resolve(request.StrategyId).Settings;
        var run = new GridBacktestRun
        {
            StrategyId = result.StrategyId, MarketDataSource = "Mt5Exness", Symbol = result.Symbol, BrokerSymbol = spec.BrokerSymbol,
            Interval = result.Interval, RequestedStartUtc = request.RequestedStartUtc!.Value, RequestedEndUtc = request.RequestedEndUtc!.Value,
            ActualStartUtc = result.ActualStartUtc, ActualEndUtc = result.ActualEndUtc, CreatedAtUtc = createdAt,
            CompletedAtUtc = DateTimeOffset.UtcNow, Status = "Completed", AccountCurrency = result.AccountCurrency,
            StartingBalance = result.StartingBalance, EndingBalance = result.EndingBalance,
            GridBasketRiskPercent = settings.GridBasketRiskPercent, LevelCount = settings.LevelCount, CooldownBars = settings.CooldownBars, RangeLookback = 50, AtrPeriod = 14, AdxPeriod = 14,
            AdxThreshold = 20m, AtrSpacingMultiplier = .50m, CommissionPerLotPerSide = request.PaperCommissionPerLotPerSide,
            HistoricalSpreadModel = Mt5HistoricalBacktestEngine.SpreadModel, HistoricalChartMode = spec.HistoricalChartMode.ToString(),
            ContractSize = spec.ContractSize, VolumeMin = spec.VolumeMin, VolumeMax = spec.VolumeMax, VolumeStep = spec.VolumeStep,
            VolumeLimit = spec.VolumeLimit, PointSize = spec.PointSize, TickSize = spec.TickSize, TickValueProfit = spec.TickValueProfit,
            TickValueLoss = spec.TickValueLoss, StopsLevelPoints = spec.StopsLevelPoints, TradeMode = result.Instrument.TradeMode.ToString(),
            ReportingCandleCount = result.CandleCount, WarmupCandleCount = result.WarmupCandleCount,
            BasketCount = result.BasketCount, WinningBaskets = result.WinningBaskets, LosingBaskets = result.LosingBaskets,
            BreakEvenBaskets = result.BreakEvenBaskets, LongBaskets = result.LongBaskets, ShortBaskets = result.ShortBaskets,
            GrossPnl = result.GrossPnl, TotalCommission = result.Commission, NetPnl = result.NetPnl, AverageNetPnl = result.AverageNetPnl,
            MaxDrawdown = result.MaxDrawdown, GrossProfitFactor = result.GrossProfitFactor, NetProfitFactor = result.NetProfitFactor,
            QualifiedCycles = d.QualifiedCycles, RejectedQualificationCount = d.RejectedQualificationCount,
            AmbiguousFirstSideCount = d.AmbiguousFirstSideCount, NoFillCyclesAtEndOfData = d.NoFillCyclesAtEndOfData,
            TradeModeBlockedCount = d.TradeModeBlockedCount, RiskBelowMinimumVolumeCount = d.RiskBelowMinimumVolumeCount,
            RiskCannotBeSafelySizedCount = d.RiskCannotBeSafelySizedCount, RiskCalculationUnavailableCount = d.RiskCalculationUnavailableCount,
            MarginCalculationUnavailableCount = d.MarginCalculationUnavailableCount, InsufficientMarginCount = d.InsufficientMarginCount,
            EconomicsCallCount = result.EconomicsCallCount, EconomicsElapsedMilliseconds = result.EconomicsElapsedMilliseconds
        };
        var byTime = new Dictionary<DateTimeOffset, GridBacktestCycle>();
        foreach (var c in result.Cycles)
        {
            var i = c.Indicators;
            var row = new GridBacktestCycle { Sequence = run.Cycles.Count, QualificationTimeUtc = i.Time,
                RangeHigh = i.RangeHigh, RangeLow = i.RangeLow, QualificationClose = i.Close, Atr = i.Atr!.Value, Adx = i.Adx!.Value,
                Anchor = c.Anchor, Spacing = c.Spacing, LongStop = c.LongStop, ShortStop = c.ShortStop, EntryEquity = c.EntryEquity,
                TargetRiskPercent = c.TargetRiskPercent, TargetRiskAmount = c.Sizing.TargetRiskAmount, CommonLots = c.Sizing.Lots,
                LongRisk = c.Sizing.LongRisk, ShortRisk = c.Sizing.ShortRisk, LongMargin = c.Sizing.LongMargin, ShortMargin = c.Sizing.ShortMargin,
                AllowedDirections = c.Sizing.AllowedDirections.ToString(),
                PlannedLevels = c.PlannedLevels.Select(l => new GridBacktestPlannedLevel { LevelNumber = l.Number,
                    Direction = l.Direction.ToString(), Price = l.Price, Lots = l.Lots, InitialStopRisk = l.InitialStopRisk,
                    RequiredMargin = l.RequiredMargin, Allowed = l.Allowed }).ToList() };
            run.Cycles.Add(row); byTime.Add(i.Time, row);
        }
        foreach (var b in result.Baskets)
        {
            var cycle = byTime[b.Cycle.Indicators.Time];
            cycle.Baskets.Add(new() { Direction = b.Direction.ToString(), ExitReason = b.ExitReason.ToString(), ExitTimeUtc = b.ExitTime,
                ExitPrice = b.ExitPrice, ExitSpread = b.ExitSpread, GrossPnl = b.GrossPnl, Commission = b.Commission, NetPnl = b.NetPnl,
                EndingBalance = b.EndingBalance, UsedMargin = b.UsedMargin, ActualFilledInitialStopRisk = b.ActualFilledInitialStopRisk,
                PlannedWorstCasePriceRisk = b.PlannedWorstCasePriceRisk, PlannedWorstCaseMargin = b.PlannedWorstCaseMargin,
                Legs = b.Legs.Select(l => new GridBacktestLeg { LevelNumber = l.Level, Direction = b.Direction.ToString(),
                    PlannedPrice = cycle.PlannedLevels.Single(p => p.LevelNumber == l.Level && p.Direction == b.Direction.ToString()).Price,
                    FillTimeUtc = l.FillTime, FillPrice = l.FillPrice, Lots = l.Lots, RequiredMargin = l.RequiredMargin,
                    InitialStopRisk = l.InitialStopRisk, EntryCommission = l.EntryCommission, ExitCommission = l.ExitCommission,
                    TotalCommission = l.Commission, GrossPnl = l.GrossPnl, NetPnl = l.NetPnl }).ToList() });
        }
        run.Events = result.Events.Select((e, index) => new GridBacktestEvent { Sequence = index, Time = e.Time, Type = e.Type.ToString(),
            Level = e.Level, ExecutablePrice = e.ExecutablePrice, Detail = e.Detail }).ToList();
        // Do not persist raw provider exception text (which can contain credentials).
        run.Diagnostics = d.Entries.Select((e, index) => new GridBacktestDiagnostic { Sequence = index, Time = e.Time,
            Code = e.Code, DomainCode = e.DomainCode?.ToString(), Operation = e.Economics?.Operation,
            BrokerSymbol = e.Economics?.Symbol, Direction = e.Economics?.Direction.ToString(), Lots = e.Economics?.Lots,
            Entry = e.Economics?.Entry, Stop = e.Economics?.Stop, Detail = e.Economics is null ? e.Detail : "Native economics calculation unavailable." }).ToList();
        return run;
    }
}
