using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Builds a ProjectCheckReport via on-demand scanning of the project's text
/// files plus the registry's sidecar pairing snapshot. Pure read; no FS
/// mutation. Performance is bounded by scan time; there is no precomputed
/// reference index waiting in memory.
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
