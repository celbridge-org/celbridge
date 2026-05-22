using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Builds a ProjectCheckReport from the metadata service's reference graph and
/// the registry's sidecar pairing snapshot. Pure read; no FS mutation.
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
        OrphanSidecars: Array.Empty<OrphanSidecar>(),
        BrokenSidecars: Array.Empty<BrokenSidecar>());

    public override async Task<Result> ExecuteAsync()
    {
        var workspaceService = _workspaceWrapper.WorkspaceService;
        var registry = workspaceService.ResourceService.Registry;
        var metaData = workspaceService.ResourceMetaData;

        // Reference graph and sidecar report are both in-memory after the
        // initial rebuild completes. Block the call on readiness so the check
        // never returns a partial view of the project.
        await metaData.WaitUntilReadyAsync();

        var brokenReferences = new List<BrokenReference>();
        foreach (var target in metaData.GetAllReferencedTargets())
        {
            var resourceResult = registry.GetResource(target);
            if (resourceResult.IsSuccess)
            {
                continue;
            }
            foreach (var source in metaData.GetReferencers(target))
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
        var orphanSidecars = sidecarReport.Orphan
            .OrderBy(k => k.ToString(), StringComparer.Ordinal)
            .Select(k => new OrphanSidecar(k))
            .ToList();
        var brokenSidecars = sidecarReport.Broken
            .OrderBy(k => k.ToString(), StringComparer.Ordinal)
            .Select(k => new BrokenSidecar(k))
            .ToList();

        ResultValue = new ProjectCheckReport(
            BrokenReferences: brokenReferences,
            OrphanSidecars: orphanSidecars,
            BrokenSidecars: brokenSidecars);

        return Result.Ok();
    }
}
