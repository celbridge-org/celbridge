using Celbridge.Logging;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

/// <summary>
/// Cascades the paired sidecar of a structural operation: when the parent
/// file moves, copies, or deletes, the sidecar follows. The parent operation
/// has already succeeded by the time the cascade runs, so a failure surfaces
/// as a SidecarOutcome on the parent's result. The cascade records its
/// changes with the parent operation's recorder.
/// </summary>
internal sealed class SidecarCascade
{
    private readonly ILogger _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILocalFileSystem _fileSystem;

    public SidecarCascade(
        ILogger logger,
        IWorkspaceWrapper workspaceWrapper,
        ILocalFileSystem fileSystem)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _fileSystem = fileSystem;
    }

    public async Task<SidecarOutcome> TryMoveAsync(ResourceKey source, ResourceKey dest, ChangeRecorder changes)
    {
        var sourceSidecar = AppendSidecarSuffix(source);
        var destSidecar = AppendSidecarSuffix(dest);
        if (sourceSidecar is null
            || destSidecar is null)
        {
            return SidecarOutcome.NotPresent;
        }

        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;

        var resolveSourceResult = rootHandlerRegistry.ResolveResourcePath(sourceSidecar.Value);
        if (resolveSourceResult.IsFailure)
        {
            return SidecarOutcome.NotPresent;
        }
        var sourceSidecarPath = resolveSourceResult.Value;
        var sourceInfoResult = await _fileSystem.GetInfoAsync(sourceSidecarPath);
        if (sourceInfoResult.IsFailure
            || sourceInfoResult.Value.Kind != StorageItemKind.File)
        {
            return SidecarOutcome.NotPresent;
        }

        string destSidecarPath;
        if (LetterCaseHelper.DiffersOnlyInNameCase(sourceSidecar.Value, destSidecar.Value))
        {
            // The sidecar is still stored under the old name, so resolving the destination would refuse the new
            // letter case. The destination's path is the source sidecar's path with the new name.
            destSidecarPath = Path.Combine(Path.GetDirectoryName(sourceSidecarPath)!, destSidecar.Value.ResourceName);
        }
        else
        {
            var resolveDestResult = rootHandlerRegistry.ResolveResourcePath(destSidecar.Value);
            if (resolveDestResult.IsFailure)
            {
                _logger.LogWarning($"Failed to resolve sidecar destination '{destSidecar}' for move from '{source}'. Sidecar bytes remain at the source path.");
                return SidecarOutcome.Failed;
            }
            destSidecarPath = resolveDestResult.Value;

            var destInfoResult = await _fileSystem.GetInfoAsync(destSidecarPath);
            if (destInfoResult.IsSuccess
                && destInfoResult.Value.Kind != StorageItemKind.NotFound)
            {
                _logger.LogWarning($"Sidecar destination '{destSidecar}' already exists. Parent move completed but sidecar was not cascaded.");
                return SidecarOutcome.Failed;
            }
        }

        var destFolder = Path.GetDirectoryName(destSidecarPath);
        if (!string.IsNullOrEmpty(destFolder))
        {
            var createFolderResult = await _fileSystem.CreateFolderAsync(destFolder);
            if (createFolderResult.IsFailure)
            {
                _logger.LogWarning($"Failed to create sidecar destination folder '{destFolder}' for move from '{sourceSidecar}'.");
                return SidecarOutcome.Failed;
            }
        }

        var moveResult = await _fileSystem.MoveFileAsync(sourceSidecarPath, destSidecarPath);
        if (moveResult.IsFailure)
        {
            _logger.LogWarning($"Failed to cascade sidecar move from '{sourceSidecar}' to '{destSidecar}'. {moveResult.DiagnosticReport}");
            return SidecarOutcome.Failed;
        }

        changes.RecordRemoved(sourceSidecar.Value, sourceSidecarPath);
        changes.RecordCreated(destSidecar.Value, destSidecarPath);

        return SidecarOutcome.Cascaded;
    }

    public async Task<SidecarOutcome> TryCopyAsync(ResourceKey source, ResourceKey dest, ChangeRecorder changes)
    {
        var sourceSidecar = AppendSidecarSuffix(source);
        var destSidecar = AppendSidecarSuffix(dest);
        if (sourceSidecar is null
            || destSidecar is null)
        {
            return SidecarOutcome.NotPresent;
        }

        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;

        var resolveSourceResult = rootHandlerRegistry.ResolveResourcePath(sourceSidecar.Value);
        if (resolveSourceResult.IsFailure)
        {
            return SidecarOutcome.NotPresent;
        }
        var sourceSidecarPath = resolveSourceResult.Value;
        var sourceInfoResult = await _fileSystem.GetInfoAsync(sourceSidecarPath);
        if (sourceInfoResult.IsFailure
            || sourceInfoResult.Value.Kind != StorageItemKind.File)
        {
            return SidecarOutcome.NotPresent;
        }

        var resolveDestResult = rootHandlerRegistry.ResolveResourcePath(destSidecar.Value);
        if (resolveDestResult.IsFailure)
        {
            _logger.LogWarning($"Failed to resolve sidecar destination '{destSidecar}' for copy from '{source}'.");
            return SidecarOutcome.Failed;
        }
        var destSidecarPath = resolveDestResult.Value;

        var destInfoResult = await _fileSystem.GetInfoAsync(destSidecarPath);
        if (destInfoResult.IsSuccess
            && destInfoResult.Value.Kind != StorageItemKind.NotFound)
        {
            _logger.LogWarning($"Sidecar destination '{destSidecar}' already exists. Parent copy completed but sidecar was not cascaded.");
            return SidecarOutcome.Failed;
        }

        var destFolder = Path.GetDirectoryName(destSidecarPath);
        if (!string.IsNullOrEmpty(destFolder))
        {
            var createFolderResult = await _fileSystem.CreateFolderAsync(destFolder);
            if (createFolderResult.IsFailure)
            {
                _logger.LogWarning($"Failed to create sidecar destination folder '{destFolder}' for copy from '{sourceSidecar}'.");
                return SidecarOutcome.Failed;
            }
        }

        var copyResult = await _fileSystem.CopyFileAsync(sourceSidecarPath, destSidecarPath);
        if (copyResult.IsFailure)
        {
            _logger.LogWarning($"Failed to cascade sidecar copy from '{sourceSidecar}' to '{destSidecar}'. {copyResult.DiagnosticReport}");
            return SidecarOutcome.Failed;
        }

        changes.RecordCreated(destSidecar.Value, destSidecarPath);

        return SidecarOutcome.Cascaded;
    }

    public async Task<SidecarOutcome> TryDeleteAsync(ResourceKey source, ChangeRecorder changes)
    {
        var sourceSidecar = AppendSidecarSuffix(source);
        if (sourceSidecar is null)
        {
            return SidecarOutcome.NotPresent;
        }

        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;

        var resolveResult = rootHandlerRegistry.ResolveResourcePath(sourceSidecar.Value);
        if (resolveResult.IsFailure)
        {
            return SidecarOutcome.NotPresent;
        }
        var sidecarPath = resolveResult.Value;
        var sidecarInfoResult = await _fileSystem.GetInfoAsync(sidecarPath);
        if (sidecarInfoResult.IsFailure
            || sidecarInfoResult.Value.Kind != StorageItemKind.File)
        {
            return SidecarOutcome.NotPresent;
        }

        var deleteResult = await _fileSystem.DeleteFileAsync(sidecarPath);
        if (deleteResult.IsFailure)
        {
            _logger.LogWarning($"Failed to cascade sidecar delete for '{sourceSidecar}'. {deleteResult.DiagnosticReport}");
            return SidecarOutcome.Failed;
        }

        changes.RecordRemoved(sourceSidecar.Value, sidecarPath);

        return SidecarOutcome.Cascaded;
    }

    // Returns the sidecar resource key for the given parent, or null when no
    // valid sidecar key can be derived (root-only key, or the parent is itself
    // a .cel file).
    private ResourceKey? AppendSidecarSuffix(ResourceKey key)
    {
        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;
        var result = sidecarService.GetSidecarKey(key);
        if (result.IsSuccess)
        {
            return result.Value;
        }
        return null;
    }
}
