using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Services;

// This export is intentionally a projection of durable Demo strategy and broker ledgers.
// It does not contact MT5, infer fills, calculate historical P/L, or expose account identity.
public static class DemoStrategySessionExcelExport
{
    public static async Task<byte[]> CreateAsync(EmaBotDbContext database, int sessionId, CancellationToken token)
    {
        var session = await database.DemoStrategySessions.AsNoTracking()
            .Include(item => item.Symbols).ThenInclude(item => item.Intents).ThenInclude(item => item.DemoExecution).ThenInclude(item => item!.ManagementActions)
            .Include(item => item.PositionManagement)
            .SingleAsync(item => item.Id == sessionId, token);
        var intents = session.Symbols.SelectMany(item => item.Intents).OrderBy(item => item.Id).ToArray();
        var linked = intents.Where(item => item.DemoExecution is not null).Select(item => new LinkedIntent(item, item.DemoExecution!)).DistinctBy(item => item.Execution.Id).OrderBy(item => item.Execution.Id).ToArray();
        var byExecution = linked.ToDictionary(item => item.Execution.Id, item => item.Intent);
        var budget = DemoStrategySessionBudgetEvaluator.Evaluate(session.InitialAllocation, linked.Select(item => item.Execution));
        var sheets = new List<Sheet>
        {
            new("SESSION SUMMARY", SummaryRows(session, intents, linked, budget)),
            new("INTENTS AND REASONING", IntentRows(session, intents)),
            new("BROKER EXECUTIONS", ExecutionRows(session, linked)),
            new("POSITION MANAGEMENT", ManagementRows(session.PositionManagement.OrderBy(item => item.Id))),
            new("MANAGEMENT ACTIONS", ActionRows(session, linked, byExecution)),
            new("BROKER PNL EVIDENCE", PnlRows(session, linked))
        };
        return Workbook(sheets);
    }

    private static IEnumerable<object?[]> SummaryRows(DemoStrategySession session, IReadOnlyList<DemoStrategyIntent> intents, IReadOnlyList<LinkedIntent> linked, DemoStrategySessionBudgetEvidence budget)
    {
        var openOrUnresolved = linked.Count(item => item.Execution.State is not DemoExecutionState.Closed and not DemoExecutionState.Rejected and not DemoExecutionState.Cancelled);
        return Fields(
            ("ExportedAtUtc", DateTimeOffset.UtcNow), ("SessionId", session.Id), ("Status", session.Status), ("Symbol", string.Join(", ", session.Symbols.Select(item => item.Symbol))), ("BrokerSymbol", string.Join(", ", session.Symbols.Select(item => item.BrokerSymbol))), ("Timeframe", session.Interval),
            ("CreatedAtUtc", session.CreatedAtUtc), ("StartedAtUtc", session.StartedAtUtc), ("StoppedAtUtc", session.StoppedAtUtc), ("InterruptedAtUtc", session.InterruptedAtUtc), ("FailureMessage", session.FailureMessage), ("NewEntriesPaused", session.NewEntriesPaused), ("NewEntriesPausedAtUtc", session.NewEntriesPausedAtUtc), ("InitialAllocation", session.InitialAllocation),
            ("BudgetAccountCurrency", budget.AccountCurrency), ("BudgetRealizedPnl", budget.RealizedPnl), ("BudgetUnrealizedPnl", budget.UnrealizedPnl), ("BudgetBalance", budget.Balance), ("BudgetEquity", budget.Equity), ("BudgetEvidenceReady", budget.EvidenceReady), ("BudgetReason", budget.Reason),
            ("AutomationEnabledAtCreation", session.AutomationEnabledAtCreation), ("FixedLots", session.FixedLots), ("RiskReward", session.RiskReward), ("MinEmaGapPercent", session.MinEmaGapPercent), ("MaxStopDistancePercent", session.MaxStopDistancePercent), ("WaitForConfirmationCandle", session.WaitForConfirmationCandle), ("UseEma100Filter", session.UseEma100Filter), ("UseAdaptiveInitialStop", session.UseAdaptiveInitialStop), ("TrailingStopEnabled", session.TrailingStopEnabled), ("ExitOnOppositeCrossover", session.ExitOnOppositeCrossover), ("SameTrendReentryEnabled", session.SameTrendReentryEnabled), ("MaxReentryAgeBars", session.MaxReentryAgeBars),
            ("TotalIntents", intents.Count), ("ExecutedLinkedIntents", linked.Count), ("BlockedIntents", intents.Count(item => item.Status == DemoStrategyIntentStatus.Blocked)), ("ExpiredIntents", intents.Count(item => item.Status == DemoStrategyIntentStatus.Expired)), ("ReentryIntents", intents.Count(item => item.IsReentry)), ("BrokerExecutions", linked.Count), ("ClosedExecutions", linked.Count(item => item.Execution.State == DemoExecutionState.Closed)), ("OpenOrUnresolvedExecutions", openOrUnresolved), ("ManagementRecords", session.PositionManagement.Count), ("ManagementActions", linked.Sum(item => item.Execution.ManagementActions.Count)),
            ("Note", "Logical session allocation is application-enforced and is not a broker sub-account or guaranteed maximum loss."));
    }

    private static IEnumerable<object?[]> IntentRows(DemoStrategySession session, IEnumerable<DemoStrategyIntent> intents)
    {
        var header = new object?[] { "IntentId", "SessionId", "SessionSymbolId", "Symbol", "BrokerSymbol", "Interval", "EntryType", "Direction", "Status", "Reason", "CrossoverTimeUtc", "SignalTimeUtc", "ExpectedEntryOpenUtc", "SignalOpen", "SignalClose", "SignalEma9", "SignalEma15", "SignalEma100", "SignalGapPercent", "SignalGapState", "StructuralStopLoss", "StopSourceType", "StopSourceTimeUtc", "IntendedTakeProfit", "IntendedVolumeLots", "ClientExecutionId", "DemoExecutionId", "IsReentry", "ReentrySourceDemoExecutionId", "TrendRegimeCrossoverTimeUtc", "ReentryAgeBars", "CreatedAtUtc", "UpdatedAtUtc", "SubmittedAtUtc", "WaitForConfirmationCandle", "UseEma100Filter", "UseAdaptiveInitialStop", "RiskReward", "MinEmaGapPercent", "MaxStopDistancePercent" };
        return new[] { header }.Concat(intents.Select(intent =>
        {
            var symbol = session.Symbols.Single(item => item.Id == intent.DemoStrategySessionSymbolId);
            return new object?[] { intent.Id, session.Id, intent.DemoStrategySessionSymbolId, symbol.Symbol, symbol.BrokerSymbol, session.Interval, intent.IsReentry ? "Re-entry" : "Normal", intent.Direction, intent.Status, intent.Reason, intent.CrossoverTimeUtc, intent.SignalTimeUtc, intent.ExpectedEntryOpenUtc, intent.SignalOpen, intent.SignalClose, intent.SignalEma9, intent.SignalEma15, intent.SignalEma100, intent.SignalGapPercent, intent.SignalGapState, intent.StructuralStopLoss, intent.StopSourceType, intent.StopSourceTimeUtc, intent.IntendedTakeProfit, intent.IntendedVolumeLots, intent.ClientExecutionId, intent.DemoExecutionId, intent.IsReentry, intent.ReentrySourceDemoExecutionId, intent.TrendRegimeCrossoverTimeUtc, intent.ReentryAgeBars, intent.CreatedAtUtc, intent.UpdatedAtUtc, intent.SubmittedAtUtc, session.WaitForConfirmationCandle, session.UseEma100Filter, session.UseAdaptiveInitialStop, session.RiskReward, session.MinEmaGapPercent, session.MaxStopDistancePercent };
        }));
    }

    private static IEnumerable<object?[]> ExecutionRows(DemoStrategySession session, IEnumerable<LinkedIntent> linked)
    {
        var header = new object?[] { "ExecutionId", "IntentId", "SessionId", "ClientExecutionId", "BrokerSymbol", "Side", "State", "Provider", "VolumeLots", "FilledVolumeLots", "AverageFillPrice", "ClosedVolumeLots", "AverageClosePrice", "RequestedStopLoss", "RequestedTakeProfit", "CurrentStopLoss", "CurrentTakeProfit", "ProtectionObservedAtUtc", "PositionTicket", "PositionIdentifier", "OrderTicket", "EntryDealTicket", "ExitDealTicket", "NativeExitReason", "NativeExitReasonConflicted", "BrokerExecutedAtUtc", "BrokerClosedAtUtc", "BrokerRetcode", "BrokerMessage", "CreatedAtUtc", "PreflightAtUtc", "SubmittedAtUtc", "BrokerAcceptedAtUtc", "ClosedAtUtc", "ReconciledAtUtc", "ReconciliationSource", "ReconciliationNote", "EvaluatedBrokerPnl", "EvaluatedBrokerPnlEvidenceType", "BrokerAccountCurrency", "EvidenceAvailable", "EvidenceReason" };
        return new[] { header }.Concat(linked.Select(link =>
        {
            var item = link.Execution; var evidence = DemoExecutionBrokerMoneyEvidenceEvaluator.Evaluate(item);
            return new object?[] { item.Id, link.Intent.Id, session.Id, item.ClientExecutionId, item.BrokerSymbol, item.Side, item.State, item.Provider, item.VolumeLots, item.FilledVolumeLots, item.AverageFillPrice, item.ClosedVolumeLots, item.AverageClosePrice, item.RequestedStopLoss, item.RequestedTakeProfit, item.CurrentStopLoss, item.CurrentTakeProfit, item.ProtectionObservedAtUtc, item.PositionTicket, item.PositionIdentifier, item.OrderTicket, item.EntryDealTicket, item.ExitDealTicket, item.NativeExitReason, item.NativeExitReasonConflicted, item.BrokerExecutedAtUtc, item.BrokerClosedAtUtc, item.BrokerRetcode, item.BrokerMessage, item.CreatedAtUtc, item.PreflightAtUtc, item.SubmittedAtUtc, item.BrokerAcceptedAtUtc, item.ClosedAtUtc, item.ReconciledAtUtc, item.ReconciliationSource, item.ReconciliationNote, evidence.Available ? evidence.Amount : null, evidence.Available ? (item.State == DemoExecutionState.Closed ? "Closed broker-history evidence" : "Open broker evidence") : null, item.BrokerAccountCurrency, evidence.Available, evidence.Reason };
        }));
    }

    private static IEnumerable<object?[]> ManagementRows(IEnumerable<DemoStrategyPositionManagement> management)
    {
        var header = new object?[] { "Id", "SessionId", "SessionSymbolId", "IntentId", "ExecutionId", "State", "OriginalEntryPrice", "OriginalStopLoss", "OriginalTakeProfit", "BestFavorablePrice", "BestFavorableProgressPercent", "TakeProfitExtensionState", "TargetExtensionAppliedAtUtc", "HighestAttemptedLockPercent", "HighestAppliedLockPercent", "PendingProtectionActionId", "PendingProtectionLockPercent", "PendingProtectionExtendsTarget", "PendingDesiredStopLoss", "PendingDesiredTakeProfit", "OppositeSignalTimeUtc", "OppositeSignalDirection", "OppositeCloseState", "OppositeCloseRequestedAtUtc", "LastManagedAtUtc", "LastReason", "CreatedAtUtc", "UpdatedAtUtc" };
        return new[] { header }.Concat(management.Select(item => new object?[] { item.Id, item.DemoStrategySessionId, item.DemoStrategySessionSymbolId, item.DemoStrategyIntentId, item.DemoExecutionId, item.State, item.OriginalEntryPrice, item.OriginalStopLoss, item.OriginalTakeProfit, item.BestFavorablePrice, item.BestFavorableProgressPercent, item.TakeProfitExtensionState, item.TargetExtensionAppliedAtUtc, item.HighestAttemptedLockPercent, item.HighestAppliedLockPercent, item.PendingProtectionActionId, item.PendingProtectionLockPercent, item.PendingProtectionExtendsTarget, item.PendingDesiredStopLoss, item.PendingDesiredTakeProfit, item.OppositeSignalTimeUtc, item.OppositeSignalDirection, item.OppositeCloseState, item.OppositeCloseRequestedAtUtc, item.LastManagedAtUtc, item.LastReason, item.CreatedAtUtc, item.UpdatedAtUtc }));
    }

    private static IEnumerable<object?[]> ActionRows(DemoStrategySession session, IEnumerable<LinkedIntent> linked, IReadOnlyDictionary<int, DemoStrategyIntent> byExecution)
    {
        var header = new object?[] { "ActionId", "ExecutionId", "IntentId", "SessionId", "ClientManagementActionId", "Kind", "State", "RequestedStopLoss", "RequestedTakeProfit", "ObservedBeforeStopLoss", "ObservedBeforeTakeProfit", "AppliedStopLoss", "AppliedTakeProfit", "BrokerRetcode", "BrokerMessage", "CreatedAtUtc", "SubmittedAtUtc", "CompletedAtUtc", "ReconciledAtUtc", "ReconciliationSource", "ReconciliationNote" };
        return new[] { header }.Concat(linked.SelectMany(link => link.Execution.ManagementActions.OrderBy(action => action.Id).Select(action => new object?[] { action.Id, link.Execution.Id, byExecution[link.Execution.Id].Id, session.Id, action.ClientManagementActionId, action.Kind, action.State, action.RequestedStopLoss, action.RequestedTakeProfit, action.ObservedBeforeStopLoss, action.ObservedBeforeTakeProfit, action.AppliedStopLoss, action.AppliedTakeProfit, action.BrokerRetcode, action.BrokerMessage, action.CreatedAtUtc, action.SubmittedAtUtc, action.CompletedAtUtc, action.ReconciledAtUtc, action.ReconciliationSource, action.ReconciliationNote })));
    }

    private static IEnumerable<object?[]> PnlRows(DemoStrategySession session, IEnumerable<LinkedIntent> linked)
    {
        var header = new object?[] { "ExecutionId", "SessionId", "IntentId", "AccountCurrency", "BrokerEntryProfit", "BrokerEntryCommission", "BrokerEntrySwap", "BrokerEntryFee", "BrokerEntryPnlObservedAtUtc", "BrokerCurrentProfit", "BrokerCurrentSwap", "BrokerCurrentPnlObservedAtUtc", "BrokerHistoryProfit", "BrokerHistoryCommission", "BrokerHistorySwap", "BrokerHistoryFee", "BrokerHistoryPnlObservedAtUtc", "EvaluatorAvailable", "EvaluatorAmount", "EvaluatorEvidenceType", "EvaluatorReason" };
        return new[] { header }.Concat(linked.Select(link => { var item = link.Execution; var evidence = DemoExecutionBrokerMoneyEvidenceEvaluator.Evaluate(item); return new object?[] { item.Id, session.Id, link.Intent.Id, item.BrokerAccountCurrency, item.BrokerEntryProfit, item.BrokerEntryCommission, item.BrokerEntrySwap, item.BrokerEntryFee, item.BrokerEntryPnlObservedAtUtc, item.BrokerCurrentProfit, item.BrokerCurrentSwap, item.BrokerCurrentPnlObservedAtUtc, item.BrokerHistoryProfit, item.BrokerHistoryCommission, item.BrokerHistorySwap, item.BrokerHistoryFee, item.BrokerHistoryPnlObservedAtUtc, evidence.Available, evidence.Available ? evidence.Amount : null, evidence.Available ? (item.State == DemoExecutionState.Closed ? "Closed broker-history evidence" : "Open broker evidence") : null, evidence.Reason }; }));
    }

    private static IEnumerable<object?[]> Fields(params (string Field, object? Value)[] fields) => [new object?[] { "Field", "Value" }, .. fields.Select(item => new object?[] { item.Field, item.Value })];
    private sealed record LinkedIntent(DemoStrategyIntent Intent, DemoExecution Execution);
    private sealed record Sheet(string Name, IEnumerable<object?[]> Rows);
    private static byte[] Workbook(IReadOnlyList<Sheet> sheets)
    {
        using var stream = new MemoryStream(); using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Write(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" + string.Concat(Enumerable.Range(1, sheets.Count).Select(i => $"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>")) + "</Types>");
            Write(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Write(zip, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>" + string.Concat(sheets.Select((sheet, i) => $"<sheet name=\"{SecurityElement.Escape(sheet.Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>")) + "</sheets></workbook>");
            Write(zip, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + string.Concat(Enumerable.Range(1, sheets.Count).Select(i => $"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>")) + "</Relationships>");
            foreach (var (sheet, index) in sheets.Select((sheet, index) => (sheet, index))) Write(zip, $"xl/worksheets/sheet{index + 1}.xml", Worksheet(sheet.Rows));
        }
        return stream.ToArray();
    }
    private static string Worksheet(IEnumerable<object?[]> rows) { var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>"); foreach (var row in rows) { xml.Append("<row>"); foreach (var value in row) Cell(xml, value); xml.Append("</row>"); } return xml.Append("</sheetData></worksheet>").ToString(); }
    private static void Cell(StringBuilder xml, object? value) { if (value is null) { xml.Append("<c/>"); return; } if (value is DateTimeOffset time) { xml.Append("<c t=\"inlineStr\"><is><t>").Append(time.ToString("O", CultureInfo.InvariantCulture)).Append("</t></is></c>"); return; } if (value is decimal or int or long) { xml.Append("<c><v>").Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append("</v></c>"); return; } if (value is bool flag) { xml.Append("<c t=\"b\"><v>").Append(flag ? '1' : '0').Append("</v></c>"); return; } xml.Append("<c t=\"inlineStr\"><is><t>").Append(SecurityElement.Escape(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)).Append("</t></is></c>"); }
    private static void Write(ZipArchive archive, string path, string text) { using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false)); writer.Write(text); }
}
