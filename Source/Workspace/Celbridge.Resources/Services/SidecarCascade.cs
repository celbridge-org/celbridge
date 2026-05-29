using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

/// <summary>
/// Cascades the paired sidecar of a structural operation: when the parent
/// file moves, copies, or deletes, the sidecar follows. The parent operation
/// has already succeeded by the time the cascade runs, so failure surfaces
/// as a SidecarOutcome on the parent's result rather than aborting.
/// </summary>
internal sealed class SidecarCascade
{
    private readonly ILogger _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public SidecarCascade(ILogger logger, IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public async Task<SidecarOutcome> TryMoveAsync(ResourceKey source, ResourceKey dest)
    {
        var sourceSidecar = AppendSidecarSuffix(source);
        var destSidecar = AppendSidecarSuffix(dest);
        if (sourceSidecar is null
            || destSidecar is null)
        {
            return SidecarOutcome.NotPresent;
        }

        var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveSourceResult = registry.ResolveResourcePath(sourceSidecar.Value);
        if (resolveSourceResult.IsFailure)
        {
            return SidecarOutcome.NotPresent;
        }
        var sourceSidecarPath = resolveSourceResult.Value;
        if (!File.Exists(sourceSidecarPath))
        {
            return SidecarOutcome.NotPresent;
        }

        var resolveDestResult = registry.ResolveResourcePath(destSidecar.Value);
        if (resolveDestResult.IsFailure)
        {
            _logger.LogWarning($"Failed to resolve sidecar destination '{destSidecar}' for move from '{source}'. Sidecar bytes remain at the source path.");
            return SidecarOutcome.Failed;
        }
        var destSidecarPath = resolveDestResult.Value;

        if (File.Exists(destSidecarPath))
        {
            _logger.LogWarning($"Sidecar destination '{destSidecar}' already exists. Parent move completed but sidecar was not cascaded.");
            return SidecarOutcome.Failed;
        }

        try
        {
            var destFolder = Path.GetDirectoryName(destSidecarPath);
            if (!string.IsNullOrEmpty(destFolder)
                && !Directory.Exists(destFolder))
            {
                Directory.CreateDirectory(destFolder);
            }

            await FileStorageInternals.RetryTransientIOAsync(_logger, "Sidecar move", sourceSidecarPath, () => File.Move(sourceSidecarPath, destSidecarPath));
            return SidecarOutcome.Cascaded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to cascade sidecar move from '{sourceSidecar}' to '{destSidecar}'.");
            return SidecarOutcome.Failed;
        }
    }

    public SidecarOutcome TryCopy(ResourceKey source, ResourceKey dest)
    {
        var sourceSidecar = AppendSidecarSuffix(source);
        var destSidecar = AppendSidecarSuffix(dest);
        if (sourceSidecar is null
            || destSidecar is null)
        {
            return SidecarOutcome.NotPresent;
        }

        var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveSourceResult = registry.ResolveResourcePath(sourceSidecar.Value);
        if (resolveSourceResult.IsFailure)
        {
            return SidecarOutcome.NotPresent;
        }
        var sourceSidecarPath = resolveSourceResult.Value;
        if (!File.Exists(sourceSidecarPath))
        {
            return SidecarOutcome.NotPresent;
        }

        var resolveDestResult = registry.ResolveResourcePath(destSidecar.Value);
        if (resolveDestResult.IsFailure)
        {
            _logger.LogWarning($"Failed to resolve sidecar destination '{destSidecar}' for copy from '{source}'.");
            return SidecarOutcome.Failed;
        }
        var destSidecarPath = resolveDestResult.Value;

        if (File.Exists(destSidecarPath))
        {
            _logger.LogWarning($"Sidecar destination '{destSidecar}' already exists. Parent copy completed but sidecar was not cascaded.");
            return SidecarOutcome.Failed;
        }

        try
        {
            var destFolder = Path.GetDirectoryName(destSidecarPath);
            if (!string.IsNullOrEmpty(destFolder)
                && !Directory.Exists(destFolder))
            {
                Directory.CreateDirectory(destFolder);
            }

            File.Copy(sourceSidecarPath, destSidecarPath);
            return SidecarOutcome.Cascaded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to cascade sidecar copy from '{sourceSidecar}' to '{destSidecar}'.");
            return SidecarOutcome.Failed;
        }
    }

    public SidecarOutcome TryDelete(ResourceKey source)
    {
        var sourceSidecar = AppendSidecarSuffix(source);
        if (sourceSidecar is null)
        {
            return SidecarOutcome.NotPresent;
        }

        var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = registry.ResolveResourcePath(sourceSidecar.Value);
        if (resolveResult.IsFailure)
        {
            return SidecarOutcome.NotPresent;
        }
        var sidecarPath = resolveResult.Value;
        if (!File.Exists(sidecarPath))
        {
            return SidecarOutcome.NotPresent;
        }

        try
        {
            File.Delete(sidecarPath);
            return SidecarOutcome.Cascaded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to cascade sidecar delete for '{sourceSidecar}'.");
            return SidecarOutcome.Failed;
        }
    }

    // Returns the sidecar resource key for the given parent, or null when no
    // valid sidecar key can be derived (root-only key, or the parent is itself
    // a .cel file).
    private ResourceKey? AppendSidecarSuffix(ResourceKey key)
    {
        var sidecarService = _workspaceWrapper.WorkspaceService.SidecarService;
        var result = sidecarService.GetSidecarKey(key);
        if (result.IsSuccess)
        {
            return result.Value;
        }
        return null;
    }
}
