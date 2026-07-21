using System.Globalization;
using System.Text;
using Celbridge.Logging;
using Celbridge.Packages;
using Celbridge.Resources;

namespace Celbridge.Projects.Services;

/// <summary>
/// In-memory accumulator of project-load state. FlushAsync writes the report
/// file from whatever has been recorded since BeginLoad.
/// </summary>
public sealed class ProjectLoadReporter : IProjectLoadReporter
{
    public const string ReportFileName = "project-load.md";

    private readonly ILocalFileSystem _fileSystem;
    private readonly ILogger<ProjectLoadReporter> _logger;

    private string _projectFilePath = string.Empty;
    private DateTimeOffset? _loadStartedAt;
    private DateTimeOffset? _loadCompletedAt;
    private MigrationResult? _migrationResult;
    private bool _userConfirmedUpgrade;
    private bool _userCancelledUpgrade;
    private bool _loadSucceeded;
    private Result? _loadResult;
    private PackageDiscoveryReport? _packageReport;
    private ProjectCheckReport? _checkReport;
    private DateTimeOffset? _checkCompletedAt;

    public ProjectLoadReporter(
        ILocalFileSystem fileSystem,
        ILogger<ProjectLoadReporter> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public void BeginLoad(string projectFilePath)
    {
        _projectFilePath = projectFilePath;
        _loadStartedAt = DateTimeOffset.UtcNow;
        _loadCompletedAt = null;
        _migrationResult = null;
        _userConfirmedUpgrade = false;
        _userCancelledUpgrade = false;
        _loadSucceeded = false;
        _loadResult = null;
        _packageReport = null;
        _checkReport = null;
        _checkCompletedAt = null;
    }

    public void RecordMigrationResult(MigrationResult result, bool userConfirmedUpgrade, bool userCancelledUpgrade)
    {
        _migrationResult = result;
        _userConfirmedUpgrade = userConfirmedUpgrade;
        _userCancelledUpgrade = userCancelledUpgrade;
    }

    public void RecordLoadOutcome(bool loadSucceeded, Result? loadResult)
    {
        _loadSucceeded = loadSucceeded;
        _loadResult = loadResult;
        _loadCompletedAt = DateTimeOffset.UtcNow;
    }

    public void RecordPackageReport(PackageDiscoveryReport report)
    {
        _packageReport = report;
    }

    public void RecordCheckReport(ProjectCheckReport report)
    {
        _checkReport = report;
        _checkCompletedAt = DateTimeOffset.UtcNow;
    }

    public async Task<string?> FlushAsync()
    {
        if (string.IsNullOrEmpty(_projectFilePath))
        {
            return null;
        }

        try
        {
            var reportFilePath = ResolveReportFilePath(_projectFilePath);
            var logsFolder = Path.GetDirectoryName(reportFilePath) ?? string.Empty;

            var createResult = await _fileSystem.CreateFolderAsync(logsFolder);
            if (createResult.IsFailure)
            {
                _logger.LogWarning(createResult, $"Failed to create logs folder for load report: '{logsFolder}'");
                return null;
            }

            var content = FormatReport();
            var writeResult = await _fileSystem.WriteAllTextAsync(reportFilePath, content);
            if (writeResult.IsFailure)
            {
                _logger.LogWarning(writeResult, $"Failed to write project load report: '{reportFilePath}'");
                return null;
            }

            return reportFilePath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to write project load report for: '{_projectFilePath}'");
            return null;
        }
    }

    private static string ResolveReportFilePath(string projectFilePath)
    {
        var projectFolder = Path.GetDirectoryName(projectFilePath) ?? string.Empty;
        return Path.Combine(projectFolder, ProjectConstants.CelbridgeFolder, ProjectConstants.LogsFolder, ReportFileName);
    }

    private string FormatReport()
    {
        var builder = new StringBuilder();

        builder.AppendLine("# Project load report");
        builder.AppendLine();
        builder.AppendLine($"- Project: `{_projectFilePath}`");
        if (_loadStartedAt is DateTimeOffset startedAt)
        {
            builder.AppendLine($"- Started: {FormatTimestamp(startedAt)}");
        }
        if (_loadCompletedAt is DateTimeOffset completedAt
            && _loadStartedAt is DateTimeOffset started)
        {
            var duration = (completedAt - started).TotalMilliseconds;
            builder.AppendLine($"- Duration: {duration:F0} ms");
            builder.AppendLine($"- Outcome: {(_loadSucceeded ? "success" : "failed")}");
        }
        else
        {
            builder.AppendLine("- Outcome: in progress");
        }
        builder.AppendLine();

        AppendLoadSection(builder);

        if (_packageReport is not null)
        {
            AppendPackagesSection(builder);
        }

        if (_checkReport is not null)
        {
            AppendCheckSection(builder);
        }

        return builder.ToString();
    }

    private void AppendLoadSection(StringBuilder builder)
    {
        builder.AppendLine("## Load");
        builder.AppendLine();

        if (_migrationResult is null)
        {
            builder.AppendLine("Migration step was not reached.");
            builder.AppendLine();
            return;
        }

        builder.AppendLine($"- Migration status: `{_migrationResult.Status}`");
        if (!string.IsNullOrEmpty(_migrationResult.OldVersion))
        {
            builder.AppendLine($"- Project version: `{_migrationResult.OldVersion}`");
        }
        if (!string.IsNullOrEmpty(_migrationResult.NewVersion))
        {
            builder.AppendLine($"- Application version: `{_migrationResult.NewVersion}`");
        }
        if (_userCancelledUpgrade)
        {
            builder.AppendLine("- User cancelled the upgrade dialog.");
        }
        else if (_userConfirmedUpgrade)
        {
            builder.AppendLine("- User confirmed the upgrade dialog.");
        }
        builder.AppendLine();

        if (_migrationResult.OperationResult.IsFailure)
        {
            AppendErrorBlock(builder, "Migration errors", _migrationResult.OperationResult);
        }

        if (_loadResult is { IsFailure: true } loadResult)
        {
            AppendErrorBlock(builder, "Load errors", loadResult);
        }
    }

    private void AppendPackagesSection(StringBuilder builder)
    {
        builder.AppendLine("## Packages");
        builder.AppendLine();

        var report = _packageReport!;
        builder.AppendLine($"- Bundled packages loaded: {report.BundledPackageCount}");
        builder.AppendLine($"- Project packages loaded: {report.ProjectPackageCount}");
        builder.AppendLine($"- Editor instances created: {report.EditorInstanceCount}");

        if (report.Failures.Count == 0
            && report.EditorInstanceFailures.Count == 0
            && report.EditorInstanceWarnings.Count == 0)
        {
            builder.AppendLine("- No load failures.");
            builder.AppendLine();
            return;
        }

        builder.AppendLine();

        if (report.Failures.Count > 0)
        {
            builder.AppendLine($"### Load failures ({report.Failures.Count})");
            builder.AppendLine();
            foreach (var failure in report.Failures)
            {
                var packageLabel = string.IsNullOrEmpty(failure.PackageName)
                    ? $"`{failure.Folder}`"
                    : $"`{failure.PackageName}` in `{failure.Folder}`";
                builder.AppendLine($"- {packageLabel}: `{failure.Reason}`");
                if (!string.IsNullOrEmpty(failure.Detail))
                {
                    builder.AppendLine($"  - {NormaliseNewlines(failure.Detail).Replace("\n", " ")}");
                }
            }
            builder.AppendLine();
        }

        if (report.EditorInstanceFailures.Count > 0)
        {
            builder.AppendLine($"### Skipped instances ({report.EditorInstanceFailures.Count})");
            builder.AppendLine();
            foreach (var failure in report.EditorInstanceFailures)
            {
                builder.AppendLine($"- `{failure.InstanceId}`: {NormaliseNewlines(failure.Detail).Replace("\n", " ")}");
            }
            builder.AppendLine();
        }

        if (report.EditorInstanceWarnings.Count > 0)
        {
            builder.AppendLine($"### Degraded instances ({report.EditorInstanceWarnings.Count})");
            builder.AppendLine();
            foreach (var warning in report.EditorInstanceWarnings)
            {
                builder.AppendLine($"- `{warning.InstanceId}`: {NormaliseNewlines(warning.Detail).Replace("\n", " ")}");
            }
            builder.AppendLine();
        }
    }

    private void AppendCheckSection(StringBuilder builder)
    {
        builder.AppendLine("## Consistency check");
        builder.AppendLine();

        if (_checkCompletedAt is DateTimeOffset checkedAt)
        {
            builder.AppendLine($"- Last run: {FormatTimestamp(checkedAt)}");
        }

        var report = _checkReport!;
        var totalFindings = report.BrokenReferences.Count
            + report.OrphanCelFiles.Count
            + report.BrokenCelFiles.Count;
        if (totalFindings == 0)
        {
            builder.AppendLine("- No findings.");
            builder.AppendLine();
            return;
        }

        if (report.BrokenReferences.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"### Broken references ({report.BrokenReferences.Count})");
            builder.AppendLine();
            foreach (var entry in report.BrokenReferences)
            {
                builder.AppendLine($"- `{entry.Source.FullKey}` references missing `{entry.MissingTarget.FullKey}`");
            }
        }
        if (report.OrphanCelFiles.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"### Orphan .cel files ({report.OrphanCelFiles.Count})");
            builder.AppendLine();
            foreach (var entry in report.OrphanCelFiles)
            {
                builder.AppendLine($"- `{entry.FullKey}`");
            }
        }
        if (report.BrokenCelFiles.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"### Broken .cel files ({report.BrokenCelFiles.Count})");
            builder.AppendLine();
            foreach (var entry in report.BrokenCelFiles)
            {
                builder.AppendLine($"- `{entry.FullKey}`");
            }
        }
        builder.AppendLine();
    }

    private static void AppendErrorBlock(StringBuilder builder, string heading, Result result)
    {
        builder.AppendLine($"### {heading}");
        builder.AppendLine();

        if (!string.IsNullOrEmpty(result.MessageChain))
        {
            builder.AppendLine("```");
            builder.AppendLine(NormaliseNewlines(result.MessageChain));
            builder.AppendLine("```");
        }

        if (!string.IsNullOrEmpty(result.DiagnosticReport))
        {
            builder.AppendLine();
            builder.AppendLine($"<details><summary>Diagnostic chain</summary>");
            builder.AppendLine();
            builder.AppendLine("```");
            builder.AppendLine(NormaliseNewlines(result.DiagnosticReport));
            builder.AppendLine("```");
            builder.AppendLine();
            builder.AppendLine("</details>");
        }
        builder.AppendLine();
    }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static string NormaliseNewlines(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd();
    }
}
