using System.Globalization;
using System.Text;
using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Builds a ProjectCheckReport via on-demand scanning of the project's text
/// files plus the registry's sidecar pairing snapshot. Pure read against the
/// project tree; writes the latest report to logs:project-check.log as a
/// side-effect so the host UI can offer a one-click "open report" affordance
/// without re-running the scan.
/// </summary>
public sealed class ProjectCheckCommand : CommandBase, IProjectCheckCommand
{
    // Stable filename overwritten on every run; the report is "latest result",
    // not a per-run history.
    private static readonly ResourceKey ReportFileResource = new("logs:project-check.log");

    private readonly ILogger<ProjectCheckCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ProjectCheckCommand(
        ILogger<ProjectCheckCommand> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public ProjectCheckReport ResultValue { get; private set; } = new ProjectCheckReport(
        BrokenReferences: Array.Empty<BrokenReference>(),
        OrphanCelFiles: Array.Empty<ResourceKey>(),
        BrokenCelFiles: Array.Empty<ResourceKey>());

    public override async Task<Result> ExecuteAsync()
    {
        var workspaceService = _workspaceWrapper.WorkspaceService;
        var registry = workspaceService.ResourceService.Registry;
        var scanner = workspaceService.ResourceScanner;

        var brokenReferences = new List<BrokenReference>();
        foreach (var target in await scanner.FindAllReferencedTargetsAsync())
        {
            var resourceResult = registry.GetResource(target);
            if (resourceResult.IsSuccess)
            {
                continue;
            }
            foreach (var source in await scanner.FindReferencersAsync(target))
            {
                brokenReferences.Add(new BrokenReference(source, target));
            }
        }

        // Deterministic ordering so test assertions and human readers see the
        // same shape every time.
        brokenReferences.Sort((a, b) =>
        {
            var byTarget = string.Compare(a.MissingTarget.ToString(), b.MissingTarget.ToString(), StringComparison.Ordinal);
            if (byTarget != 0)
            {
                return byTarget;
            }
            return string.Compare(a.Source.ToString(), b.Source.ToString(), StringComparison.Ordinal);
        });

        var celFileReport = registry.GetCelFileReport();
        var orphanCelFiles = celFileReport.Orphan
            .OrderBy(k => k.ToString(), StringComparer.Ordinal)
            .ToList();
        var brokenCelFiles = celFileReport.Broken
            .OrderBy(k => k.ToString(), StringComparer.Ordinal)
            .ToList();

        ResultValue = new ProjectCheckReport(
            BrokenReferences: brokenReferences,
            OrphanCelFiles: orphanCelFiles,
            BrokenCelFiles: brokenCelFiles);

        await WriteReportFileAsync(ResultValue);

        return Result.Ok();
    }

    // Write a human-readable snapshot of the report to logs:project-check.log.
    // Best-effort: a write failure leaves the in-memory ResultValue intact and
    // the command still succeeds — the file is a convenience artifact, not part
    // of the command's contract.
    private async Task WriteReportFileAsync(ProjectCheckReport report)
    {
        try
        {
            var fileSystem = _workspaceWrapper.WorkspaceService.ResourceFileSystem;
            var content = FormatReport(report);
            var writeResult = await fileSystem.WriteAllTextAsync(ReportFileResource, content);
            if (writeResult.IsFailure)
            {
                _logger.LogWarning(writeResult, "Failed to write project check report to '{Resource}'.", ReportFileResource);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write project check report to '{Resource}'.", ReportFileResource);
        }
    }

    private static string FormatReport(ProjectCheckReport report)
    {
        var builder = new StringBuilder();
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        builder.Append("Project consistency check - ");
        builder.AppendLine(timestamp);

        var totalFindings = report.BrokenReferences.Count
            + report.OrphanCelFiles.Count
            + report.BrokenCelFiles.Count;
        if (totalFindings == 0)
        {
            builder.AppendLine();
            builder.AppendLine("No findings.");
            return builder.ToString();
        }

        if (report.BrokenReferences.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"Broken references ({report.BrokenReferences.Count}):");
            foreach (var entry in report.BrokenReferences)
            {
                builder.AppendLine($"  '{entry.Source.FullKey}' references missing '{entry.MissingTarget.FullKey}'");
            }
        }
        if (report.OrphanCelFiles.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"Orphan .cel files ({report.OrphanCelFiles.Count}):");
            foreach (var entry in report.OrphanCelFiles)
            {
                builder.AppendLine($"  '{entry.FullKey}'");
            }
        }
        if (report.BrokenCelFiles.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"Broken .cel files ({report.BrokenCelFiles.Count}):");
            foreach (var entry in report.BrokenCelFiles)
            {
                builder.AppendLine($"  '{entry.FullKey}'");
            }
        }

        return builder.ToString();
    }
}
