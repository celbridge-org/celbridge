using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Builds a ProjectCheckReport via on-demand scanning of the project's text
/// files plus the registry's sidecar pairing snapshot. Pure read against the
/// project tree; the caller is responsible for surfacing the report.
/// </summary>
public sealed class ProjectCheckCommand : CommandBase, IProjectCheckCommand
{
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
        var scanner = workspaceService.ResourceService.Scanner;

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

        var sidecarReport = registry.GetSidecarReport();
        var orphanCelFiles = sidecarReport.Orphan
            .OrderBy(k => k.ToString(), StringComparer.Ordinal)
            .ToList();
        var brokenCelFiles = sidecarReport.Broken
            .OrderBy(k => k.ToString(), StringComparer.Ordinal)
            .ToList();

        ResultValue = new ProjectCheckReport(
            BrokenReferences: brokenReferences,
            OrphanCelFiles: orphanCelFiles,
            BrokenCelFiles: brokenCelFiles);

        await Task.CompletedTask;
        return Result.Ok();
    }
}
