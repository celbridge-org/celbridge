using System.Text.Json.Nodes;
using Celbridge.Projects;
using ClosedXML.Excel;
using Path = System.IO.Path;

namespace Celbridge.Server.Services;

/// <summary>
/// Aggregates and surfaces agent-activity analytics. Pulls per-call telemetry
/// from <see cref="ToolTelemetry"/>, joins it with the broker's tools/list
/// payload data, and emits the consolidated agent report as a multi-sheet
/// .xlsx workbook via <see cref="GenerateAsync"/>. The combined Tools sheet
/// is the natural pivot: sort by payload tokens to find expensive tools, by
/// calls to find hot tools, or compare both columns to spot tools paying
/// context cost without earning their keep. New analytics surfaces (top-N
/// queries, per-session breakdowns, cost projections) belong here so they
/// share the same payload+telemetry join.
/// </summary>
public class AgentAnalytics
{
    // The chars/4 rule is Anthropic's published rule of thumb. Not Claude's
    // actual tokeniser, but consistent across runs so trim-trend comparisons
    // remain meaningful.
    private const string TokenisationLabel = "approximate (chars/4)";

    private readonly ToolTelemetry _telemetry;
    private readonly IMcpToolBridge _toolBridge;
    private readonly IProjectService _projectService;

    public AgentAnalytics(
        ToolTelemetry telemetry,
        IMcpToolBridge toolBridge,
        IProjectService projectService)
    {
        _telemetry = telemetry;
        _toolBridge = toolBridge;
        _projectService = projectService;
    }

    public async Task<string> GenerateAsync()
    {
        var currentProject = _projectService.CurrentProject;
        if (currentProject is null)
        {
            throw new InvalidOperationException("Cannot write agent report because no project is loaded.");
        }

        var rawJson = await _toolBridge.GetRawToolsListJsonAsync();
        var payloadEntries = ParseToolsListPayload(rawJson);
        var invocations = _telemetry.Invocations;
        var generatedAt = DateTime.Now;

        var toolRows = BuildToolRows(payloadEntries, invocations);
        var namespaceRows = BuildNamespaceRows(toolRows);

        var fileName = $"agent-report-{generatedAt:yyyyMMdd-HHmmss}.xlsx";
        var filePath = Path.Combine(currentProject.ProjectFolderPath, fileName);

        await Task.Run(() => WriteWorkbook(filePath, generatedAt, payloadEntries, invocations, toolRows, namespaceRows));

        return filePath;
    }

    private static List<ToolPayloadEntry> ParseToolsListPayload(string rawJson)
    {
        var entries = new List<ToolPayloadEntry>();
        if (string.IsNullOrEmpty(rawJson))
        {
            return entries;
        }

        var toolsArray = JsonNode.Parse(rawJson) as JsonArray;
        if (toolsArray is null)
        {
            return entries;
        }

        foreach (var toolNode in toolsArray)
        {
            if (toolNode is not JsonObject toolObject)
            {
                continue;
            }

            var toolName = toolObject["name"]?.GetValue<string>() ?? string.Empty;
            var characters = toolObject.ToJsonString().Length;
            var tokens = ApproximateTokenCount(characters);
            entries.Add(new ToolPayloadEntry(toolName, ExtractNamespace(toolName), characters, tokens));
        }

        return entries;
    }

    // Joins per-tool payload data with telemetry aggregates. Tools that exist
    // in tools/list but were never called still produce a row (with zero call
    // metrics) so the reader can spot context cost without value.
    private static List<ToolReportRow> BuildToolRows(
        IReadOnlyList<ToolPayloadEntry> payloadEntries,
        IReadOnlyList<ToolInvocationRecord> invocations)
    {
        var telemetryByTool = invocations
            .GroupBy(record => record.ToolName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var rows = new List<ToolReportRow>(payloadEntries.Count);
        foreach (var entry in payloadEntries)
        {
            telemetryByTool.TryGetValue(entry.Name, out var toolInvocations);
            var calls = toolInvocations?.Count ?? 0;
            var errors = toolInvocations?.Count(record => !record.Success) ?? 0;
            var cacheMisses = toolInvocations?.Count(record => record.CacheMiss && !record.ProxyClient) ?? 0;
            var avgDuration = calls == 0 ? 0.0 : toolInvocations!.Average(record => record.DurationMilliseconds);
            var errorRate = calls == 0 ? 0.0 : (double)errors / calls;

            rows.Add(new ToolReportRow(
                Name: entry.Name,
                Namespace: entry.Namespace,
                PayloadCharacters: entry.Characters,
                PayloadTokens: entry.Tokens,
                Calls: calls,
                Errors: errors,
                ErrorRate: errorRate,
                AgentCacheMisses: cacheMisses,
                AvgDurationMilliseconds: avgDuration));
        }

        return rows
            .OrderByDescending(row => row.PayloadTokens)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static List<NamespaceReportRow> BuildNamespaceRows(IReadOnlyList<ToolReportRow> toolRows)
    {
        var aggregates = new Dictionary<string, NamespaceAggregate>(StringComparer.Ordinal);
        foreach (var row in toolRows)
        {
            aggregates.TryGetValue(row.Namespace, out var current);
            aggregates[row.Namespace] = new NamespaceAggregate(
                ToolCount: current.ToolCount + 1,
                Characters: current.Characters + row.PayloadCharacters,
                Tokens: current.Tokens + row.PayloadTokens,
                Calls: current.Calls + row.Calls,
                Errors: current.Errors + row.Errors);
        }

        var result = new List<NamespaceReportRow>(aggregates.Count);
        foreach (var pair in aggregates)
        {
            result.Add(new NamespaceReportRow(
                Namespace: pair.Key,
                ToolCount: pair.Value.ToolCount,
                PayloadCharacters: pair.Value.Characters,
                PayloadTokens: pair.Value.Tokens,
                Calls: pair.Value.Calls,
                Errors: pair.Value.Errors));
        }

        return result
            .OrderByDescending(row => row.PayloadTokens)
            .ThenBy(row => row.Namespace, StringComparer.Ordinal)
            .ToList();
    }

    private static int ApproximateTokenCount(int characterCount)
    {
        return (characterCount + 3) / 4;
    }

    // Tool names follow the namespace_method convention (e.g. "app_get_state").
    // Everything before the first underscore is the namespace; tools without an
    // underscore are bucketed under their full name.
    private static string ExtractNamespace(string toolName)
    {
        var underscoreIndex = toolName.IndexOf('_');
        if (underscoreIndex <= 0)
        {
            return toolName;
        }

        return toolName[..underscoreIndex];
    }

    private static void WriteWorkbook(
        string filePath,
        DateTime generatedAt,
        IReadOnlyList<ToolPayloadEntry> payloadEntries,
        IReadOnlyList<ToolInvocationRecord> invocations,
        IReadOnlyList<ToolReportRow> toolRows,
        IReadOnlyList<NamespaceReportRow> namespaceRows)
    {
        using var workbook = new XLWorkbook();
        WriteSummarySheet(workbook, generatedAt, payloadEntries, invocations);
        WriteToolsSheet(workbook, toolRows);
        WriteNamespacesSheet(workbook, namespaceRows);
        WriteInvocationsSheet(workbook, invocations);
        workbook.SaveAs(filePath);
    }

    private static void WriteSummarySheet(
        XLWorkbook workbook,
        DateTime generatedAt,
        IReadOnlyList<ToolPayloadEntry> payloadEntries,
        IReadOnlyList<ToolInvocationRecord> invocations)
    {
        var sheet = workbook.Worksheets.Add("Summary");

        sheet.Cell(1, 1).Value = "Generated";
        sheet.Cell(1, 2).Value = generatedAt;
        sheet.Cell(1, 2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";

        var totalPayloadChars = payloadEntries.Sum(entry => entry.Characters);
        var totalPayloadTokens = payloadEntries.Sum(entry => entry.Tokens);

        var totalInvocations = invocations.Count;
        var sessionCount = invocations.Select(record => record.SessionId).Distinct(StringComparer.Ordinal).Count();
        var uniqueToolsCalled = invocations.Select(record => record.ToolName).Distinct(StringComparer.Ordinal).Count();
        var errorCount = invocations.Count(record => !record.Success);
        var proxyCount = invocations.Count(record => record.ProxyClient);
        var agentCount = totalInvocations - proxyCount;
        var cacheMissCount = invocations.Count(record => record.CacheMiss && !record.ProxyClient);

        var statRows = new (string Label, object Value)[]
        {
            ("Tools registered", payloadEntries.Count),
            ("Total payload characters", totalPayloadChars),
            ("Total payload tokens", totalPayloadTokens),
            ("Tokeniser", TokenisationLabel),
            ("", ""),
            ("Total invocations", totalInvocations),
            ("Unique tools called", uniqueToolsCalled),
            ("Sessions observed", sessionCount),
            ("Agent-client invocations", agentCount),
            ("Proxy-client invocations", proxyCount),
            ("Errors", errorCount),
            ("Agent cache misses", cacheMissCount),
        };

        for (int statIndex = 0; statIndex < statRows.Length; statIndex++)
        {
            var rowNumber = statIndex + 3;
            sheet.Cell(rowNumber, 1).Value = statRows[statIndex].Label;
            switch (statRows[statIndex].Value)
            {
                case int intValue:
                    sheet.Cell(rowNumber, 2).Value = intValue;
                    break;
                default:
                    sheet.Cell(rowNumber, 2).Value = statRows[statIndex].Value.ToString();
                    break;
            }
        }

        sheet.Columns().AdjustToContents();
    }

    private static void WriteToolsSheet(XLWorkbook workbook, IReadOnlyList<ToolReportRow> toolRows)
    {
        var sheet = workbook.Worksheets.Add("Tools");

        string[] headers =
        [
            "Name",
            "Namespace",
            "PayloadChars",
            "PayloadTokens",
            "Calls",
            "Errors",
            "ErrorRate",
            "AgentCacheMisses",
            "AvgDurationMs",
        ];

        for (int columnIndex = 0; columnIndex < headers.Length; columnIndex++)
        {
            sheet.Cell(1, columnIndex + 1).Value = headers[columnIndex];
        }
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);

        for (int rowIndex = 0; rowIndex < toolRows.Count; rowIndex++)
        {
            var row = toolRows[rowIndex];
            var sheetRow = rowIndex + 2;
            sheet.Cell(sheetRow, 1).Value = row.Name;
            sheet.Cell(sheetRow, 2).Value = row.Namespace;
            sheet.Cell(sheetRow, 3).Value = row.PayloadCharacters;
            sheet.Cell(sheetRow, 4).Value = row.PayloadTokens;
            sheet.Cell(sheetRow, 5).Value = row.Calls;
            sheet.Cell(sheetRow, 6).Value = row.Errors;
            sheet.Cell(sheetRow, 7).Value = row.ErrorRate;
            sheet.Cell(sheetRow, 7).Style.NumberFormat.Format = "0.0%";
            sheet.Cell(sheetRow, 8).Value = row.AgentCacheMisses;
            sheet.Cell(sheetRow, 9).Value = row.AvgDurationMilliseconds;
            sheet.Cell(sheetRow, 9).Style.NumberFormat.Format = "0.0";
        }

        if (toolRows.Count > 0)
        {
            sheet.RangeUsed()?.SetAutoFilter();
        }
        sheet.Columns().AdjustToContents();
    }

    private static void WriteNamespacesSheet(XLWorkbook workbook, IReadOnlyList<NamespaceReportRow> namespaceRows)
    {
        var sheet = workbook.Worksheets.Add("Namespaces");

        string[] headers =
        [
            "Namespace",
            "ToolCount",
            "PayloadChars",
            "PayloadTokens",
            "Calls",
            "Errors",
        ];

        for (int columnIndex = 0; columnIndex < headers.Length; columnIndex++)
        {
            sheet.Cell(1, columnIndex + 1).Value = headers[columnIndex];
        }
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);

        for (int rowIndex = 0; rowIndex < namespaceRows.Count; rowIndex++)
        {
            var row = namespaceRows[rowIndex];
            var sheetRow = rowIndex + 2;
            sheet.Cell(sheetRow, 1).Value = row.Namespace;
            sheet.Cell(sheetRow, 2).Value = row.ToolCount;
            sheet.Cell(sheetRow, 3).Value = row.PayloadCharacters;
            sheet.Cell(sheetRow, 4).Value = row.PayloadTokens;
            sheet.Cell(sheetRow, 5).Value = row.Calls;
            sheet.Cell(sheetRow, 6).Value = row.Errors;
        }

        if (namespaceRows.Count > 0)
        {
            sheet.RangeUsed()?.SetAutoFilter();
        }
        sheet.Columns().AdjustToContents();
    }

    private static void WriteInvocationsSheet(XLWorkbook workbook, IReadOnlyList<ToolInvocationRecord> rows)
    {
        var sheet = workbook.Worksheets.Add("Invocations");

        string[] headers =
        [
            "TimestampUtc",
            "SessionId",
            "ClientName",
            "ClientVersion",
            "ToolName",
            "Success",
            "ErrorMessage",
            "DurationMilliseconds",
            "ArgPayloadBytes",
            "ResultPayloadBytes",
            "ProxyClient",
            "CacheMiss",
        ];

        for (int columnIndex = 0; columnIndex < headers.Length; columnIndex++)
        {
            sheet.Cell(1, columnIndex + 1).Value = headers[columnIndex];
        }
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var record = rows[rowIndex];
            var sheetRow = rowIndex + 2;
            sheet.Cell(sheetRow, 1).Value = record.TimestampUtc.UtcDateTime;
            sheet.Cell(sheetRow, 1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss.000";
            sheet.Cell(sheetRow, 2).Value = record.SessionId;
            sheet.Cell(sheetRow, 3).Value = record.ClientName;
            sheet.Cell(sheetRow, 4).Value = record.ClientVersion;
            sheet.Cell(sheetRow, 5).Value = record.ToolName;
            sheet.Cell(sheetRow, 6).Value = record.Success;
            sheet.Cell(sheetRow, 7).Value = record.ErrorMessage;
            sheet.Cell(sheetRow, 8).Value = record.DurationMilliseconds;
            sheet.Cell(sheetRow, 9).Value = record.ArgPayloadBytes;
            sheet.Cell(sheetRow, 10).Value = record.ResultPayloadBytes;
            sheet.Cell(sheetRow, 11).Value = record.ProxyClient;
            sheet.Cell(sheetRow, 12).Value = record.CacheMiss;
        }

        if (rows.Count > 0)
        {
            sheet.RangeUsed()?.SetAutoFilter();
        }
        sheet.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 200));
    }

    private record struct NamespaceAggregate(int ToolCount, int Characters, int Tokens, int Calls, int Errors);

    private record class ToolPayloadEntry(string Name, string Namespace, int Characters, int Tokens);

    private record class ToolReportRow(
        string Name,
        string Namespace,
        int PayloadCharacters,
        int PayloadTokens,
        int Calls,
        int Errors,
        double ErrorRate,
        int AgentCacheMisses,
        double AvgDurationMilliseconds);

    private record class NamespaceReportRow(
        string Namespace,
        int ToolCount,
        int PayloadCharacters,
        int PayloadTokens,
        int Calls,
        int Errors);
}
