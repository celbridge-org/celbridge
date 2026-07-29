using System.Globalization;
using System.Text;
using Celbridge.Logging;
using Celbridge.Projects;

namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// Formats the output of a workspace-load project-consistency check: logs one warning per
/// non-empty finding category and publishes a summary banner via IMessengerService.
/// </summary>
public sealed class ProjectCheckReporter
{
    // Cap the per-category enumeration so a project with many findings does
    // not flood the host log.
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
    /// Logs one warning per non-empty finding category and, when there are findings,
    /// sends a ProjectErrorMessage carrying the total finding count.
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
            var message = new ProjectErrorMessage(
                ProjectErrorType.ProjectCheckError,
                totalFindings.ToString(CultureInfo.InvariantCulture));
            _messengerService.Send(message);
        }
    }

    // Emits a single multi-line warning per category: header line followed by
    // each entry indented two spaces, with a trailing "... and N more" when
    // the list was truncated.
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
            builder.Append($"  ... and {omitted} more.");
        }

        _logger.LogWarning(builder.ToString());
    }
}
