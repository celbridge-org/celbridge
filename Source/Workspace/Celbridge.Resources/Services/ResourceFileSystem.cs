using System.Text;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

public sealed class ResourceFileSystem : IResourceFileSystem
{
    // Bounded retry for transient IO failures (file briefly locked by AV,
    // backup software, sync clients, concurrent writers, etc.). Total
    // worst-case wait across all attempts is BaseRetryDelayMs * (1 + 2 + ...
    // + (MaxAttempts - 1)) = 150ms with the values below.
    private const int MaxAttempts = 3;
    private const int BaseRetryDelayMs = 50;

    // Buffer size used when opening file streams. Matches the default System.IO
    // FileStream buffer size when none is supplied.
    private const int StreamBufferSize = 4096;

    private readonly ILogger<ResourceFileSystem> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    // The resource registry is workspace-scoped and transient: a constructor-
    // injected instance is a different object from the one held by ResourceService,
    // and only the ResourceService instance has ProjectFolderPath set. The
    // file-system layer resolves the live registry through the workspace wrapper
    // at call time.
    public ResourceFileSystem(
        ILogger<ResourceFileSystem> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public async Task<Result<byte[]>> ReadAllBytesAsync(ResourceKey resource)
    {
        var resolveResult = ResolvePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result<byte[]>.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        try
        {
            var bytes = await File.ReadAllBytesAsync(resourcePath);
            return Result<byte[]>.Ok(bytes);
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Fail($"Failed to read file: '{resource}'")
                .WithException(ex);
        }
    }

    public async Task<Result<string>> ReadAllTextAsync(ResourceKey resource)
    {
        var resolveResult = ResolvePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        try
        {
            var text = await File.ReadAllTextAsync(resourcePath);
            return Result<string>.Ok(text);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to read file: '{resource}'")
                .WithException(ex);
        }
    }

    public Task<Result<Stream>> OpenReadAsync(ResourceKey resource)
    {
        var resolveResult = ResolvePath(resource);
        if (resolveResult.IsFailure)
        {
            var failure = Result<Stream>.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
            return Task.FromResult(failure);
        }
        var resourcePath = resolveResult.Value;

        try
        {
            var stream = new FileStream(
                resourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                StreamBufferSize,
                useAsync: true);
            return Task.FromResult(Result<Stream>.Ok(stream));
        }
        catch (Exception ex)
        {
            var failure = Result<Stream>.Fail($"Failed to open read stream for resource: '{resource}'")
                .WithException(ex);
            return Task.FromResult(failure);
        }
    }

    public Task<Result> WriteAllBytesAsync(ResourceKey resource, byte[] bytes)
    {
        return WriteWithRetryAsync(resource, bytes);
    }

    public Task<Result> WriteAllTextAsync(ResourceKey resource, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return WriteWithRetryAsync(resource, bytes);
    }

    public Task<Result<Stream>> OpenWriteAsync(ResourceKey resource)
    {
        var resolveResult = ResolvePath(resource);
        if (resolveResult.IsFailure)
        {
            var failure = Result<Stream>.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
            return Task.FromResult(failure);
        }
        var resourcePath = resolveResult.Value;

        var ensureParentResult = EnsureParentFolderExists(resourcePath, resource);
        if (ensureParentResult.IsFailure)
        {
            var failure = Result<Stream>.Fail(ensureParentResult.FirstErrorMessage)
                .WithErrors(ensureParentResult);
            return Task.FromResult(failure);
        }

        try
        {
            // FileShare.None (not FileShare.Read) is deliberate: while a write
            // stream is open no other process can read partial bytes. The
            // trade-off is that another reader hitting the file mid-write sees
            // a sharing-violation IOException, not stale-or-partial content.
            var stream = new FileStream(
                resourcePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                StreamBufferSize,
                useAsync: true);
            return Task.FromResult(Result<Stream>.Ok(stream));
        }
        catch (Exception ex)
        {
            var failure = Result<Stream>.Fail($"Failed to open write stream for resource: '{resource}'")
                .WithException(ex);
            return Task.FromResult(failure);
        }
    }

    public Task<Result<MoveResult>> MoveAsync(ResourceKey source, ResourceKey destination)
    {
        throw new NotImplementedException("Structural operations land in Phase 1b (fs-1b).");
    }

    public Task<Result<CopyResult>> CopyAsync(ResourceKey source, ResourceKey destination)
    {
        throw new NotImplementedException("Structural operations land in Phase 1b (fs-1b).");
    }

    public Task<Result<DeleteResult>> DeleteAsync(ResourceKey source)
    {
        throw new NotImplementedException("Structural operations land in Phase 1b (fs-1b).");
    }

    public Task<Result<bool>> ExistsAsync(ResourceKey resource)
    {
        var resolveResult = ResolvePath(resource);
        if (resolveResult.IsFailure)
        {
            var failure = Result<bool>.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
            return Task.FromResult(failure);
        }
        var resourcePath = resolveResult.Value;

        var exists = File.Exists(resourcePath) || Directory.Exists(resourcePath);
        return Task.FromResult(Result<bool>.Ok(exists));
    }

    private Result<string> ResolvePath(ResourceKey resource)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        return resourceRegistry.ResolveResourcePath(resource);
    }

    private async Task<Result> WriteWithRetryAsync(ResourceKey resource, byte[] bytes)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        var ensureParentResult = EnsureParentFolderExists(resourcePath, resource);
        if (ensureParentResult.IsFailure)
        {
            return ensureParentResult;
        }

        // Stage all in-flight temp files in <project>/.celbridge/staging-fs/.
        // Centralising them keeps user-visible folders clean of orphans after
        // a crash, and the workspace wipes the folder on load to clear any
        // stragglers from a prior session. The .celbridge folder is filtered
        // by ResourceMonitor, so no spurious watcher events fire for the
        // intermediate write.
        var stagingFolder = Path.Combine(
            resourceRegistry.ProjectFolderPath,
            ProjectConstants.CelbridgeFolder,
            ProjectConstants.CelbridgeStagingFsFolder);
        try
        {
            Directory.CreateDirectory(stagingFolder);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to create staging folder: '{stagingFolder}'")
                .WithException(ex);
        }

        IOException? lastException = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await WriteAtomicAsync(resourcePath, stagingFolder, bytes);
                if (attempt > 1)
                {
                    // A retry should be unusual — the workspace owns the project
                    // folder and we use an atomic temp+rename. Surface success-
                    // after-retry as a warning so unusual disk contention (AV
                    // scans, sync clients, external locks) is visible in logs.
                    _logger.LogWarning($"Write succeeded for '{resourcePath}' on attempt {attempt} of {MaxAttempts} after transient IO failures");
                }
                return Result.Ok();
            }
            catch (IOException ex)
            {
                lastException = ex;
                if (attempt < MaxAttempts)
                {
                    var delay = BaseRetryDelayMs * attempt;
                    _logger.LogWarning(ex, $"Write attempt {attempt} failed for '{resourcePath}', retrying after {delay}ms");
                    await Task.Delay(delay);
                }
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to write file: '{resourcePath}'")
                    .WithException(ex);
            }
        }

        return Result.Fail($"Failed to write file after {MaxAttempts} attempts: '{resourcePath}'")
            .WithException(lastException!);
    }

    private static Result EnsureParentFolderExists(string resourcePath, ResourceKey resource)
    {
        var parentFolder = Path.GetDirectoryName(resourcePath);
        if (string.IsNullOrEmpty(parentFolder)
            || Directory.Exists(parentFolder))
        {
            return Result.Ok();
        }

        try
        {
            Directory.CreateDirectory(parentFolder);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to create parent folder for resource: '{resource}'")
                .WithException(ex);
        }
    }

    // Writes bytes to a uniquely-named temp file inside the project's central
    // staging folder, then atomically replaces the destination via File.Move.
    // A unique filename per write prevents concurrent writers to the same
    // destination from clobbering each other's intermediate state.
    private static async Task WriteAtomicAsync(string resourcePath, string stagingFolder, byte[] bytes)
    {
        var tempPath = Path.Combine(stagingFolder, Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes);
            File.Move(tempPath, resourcePath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best-effort cleanup. The original exception describes the real failure.
            }

            throw;
        }
    }
}
