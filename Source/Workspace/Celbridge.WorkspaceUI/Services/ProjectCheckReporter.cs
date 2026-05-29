using System.Globalization;
using System.Text;
using Celbridge.Console;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Resources;

namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// Formats and dispatches the output of a workspace-load project-consistency
/// check. Writes one multi-line warning per non-empty finding category to the
/// host log (capped per category so a project with many findings doesn't flood
/// the log) and publishes a single summary banner via IMessengerService so the
/// user notices without having to invoke data_check_project by hand.
/// </summary>
public sealed class ProjectCheckReporter
{
    // Cap the per-category enumeration so a project with many findings does
    // not flood the host log. The MCP tool data_check_project always returns
    // the full set.
    private const int MaxLoggedFindingsPerCategory = 20;

    private readonly ILogger<ProjectCheckReporter> _logger;
    private readonly IMessengerService _messengerService;

    public ProjectCheckReporter(
        ILogger<ProjectCheckReporter> logger,
        IMessengerService messengerService)
    {
        _logger = logger;
        _messengerService = messengerService;
    }

    /// <summary>
    /// Logs one warning per non-empty finding category and, when the total
    /// finding count is non-zero, sends a ConsoleErrorMessage carrying the
    /// total so the console panel can surface a dismissable warning banner.
    /// </summary>
    public void Report(ProjectCheckReport report)
    {
        if (report.BrokenReferences.Count > 0)
        {
            var entries = report.BrokenReferences
                .Select(r => $"'{r.Source.FullKey}' references missing '{r.MissingTarget.FullKey}'")
                .ToList();
            LogFindingsCategory(
                $"Project consistency check: {entries.Count} broken project: reference(s).",
                entries);
        }
        if (report.OrphanCelFiles.Count > 0)
        {
            var entries = report.OrphanCelFiles
                .Select(o => $"'{o.FullKey}'")
                .ToList();
            LogFindingsCategory(
                $"Project consistency check: {entries.Count} orphan .cel file(s).",
                entries);
        }
        if (report.BrokenCelFiles.Count > 0)
        {
            var entries = report.BrokenCelFiles
                .Select(b => $"'{b.FullKey}'")
                .ToList();
            LogFindingsCategory(
                $"Project consistency check: {entries.Count} broken .cel file(s).",
                entries);
        }

        var totalFindings = report.BrokenReferences.Count
            + report.OrphanCelFiles.Count
            + report.BrokenCelFiles.Count;
        if (totalFindings > 0)
        {
            var message = new ConsoleErrorMessage(
                ConsoleErrorType.ProjectCheckError,
                totalFindings.ToString(CultureInfo.InvariantCulture));
            _messengerService.Send(message);
        }
    }

    // Emits a single multi-line warning per category: header line followed by
    // each entry indented two spaces, with a trailing "... and N more" when
    // the list was truncated. Keeps developer-facing diagnostics in one place
    // (the host log) rather than splitting them across a count-only warning
    // and a separate MCP tool invocation.
    private void LogFindingsCategory(string headerSummary, IReadOnlyList<string> entries)
    {
        var builder = new StringBuilder();
        builder.Append(headerSummary);

        var limit = Math.Min(entries.Count, MaxLoggedFindingsPerCategory);
        for (int i = 0; i < limit; i++)
        {
            builder.AppendLine();
            builder.Append("  ");
            builder.Append(entries[i]);
        }

        if (entries.Count > MaxLoggedFindingsPerCategory)
        {
            var omitted = entries.Count - MaxLoggedFindingsPerCategory;
            builder.AppendLine();
            builder.Append($"  ... and {omitted} more (use data_check_project for the full list).");
        }

        _logger.LogWarning(builder.ToString());
    }
}
