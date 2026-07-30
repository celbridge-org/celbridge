using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Builds a ProjectCheckReport via on-demand scanning of the project's text
/// files plus the registry's sidecar pairing snapshot. Pure read against the
/// project tree; the caller is responsible for surfacing the report.
/// </summary>
public sealed class ProjectCheckCommand : CommandBase, IProjectCheckCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ProjectCheckCommand(IWorkspaceWrapper workspaceWrapper)
    {
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

        // One walk covers every target; a per-target query re-reads the whole project each time.
        var referenceIndex = await scanner.BuildReferenceIndexAsync();

        var brokenReferences = new List<BrokenReference>();
        foreach (var target in referenceIndex.ReferencedTargets)
        {
            var resourceResult = registry.GetResource(target);
            if (resourceResult.IsSuccess)
            {
                continue;
            }
            foreach (var source in referenceIndex.GetReferencers(target))
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

        return Result.Ok();
    }
}
