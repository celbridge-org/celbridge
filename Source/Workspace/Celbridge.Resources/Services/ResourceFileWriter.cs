using System.Text;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

public sealed class ResourceFileWriter : IResourceFileWriter
{
    // Bounded retry for transient IO failures (file briefly locked by AV,
    // backup software, sync clients, concurrent writers, etc.). Total
    // worst-case wait across all attempts is BaseRetryDelayMs * (1 + 2 + ...
    // + (MaxAttempts - 1)) = 150ms with the values below.
    private const int MaxAttempts = 3;
    private const int BaseRetryDelayMs = 50;

    private readonly ILogger<ResourceFileWriter> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    // The resource registry is workspace-scoped and transient: a constructor-
    // injected instance is a different object from the one held by ResourceService,
    // and only the ResourceService instance has ProjectFolderPath set. The writer
    // resolves the live registry through the workspace wrapper at call time.
    public ResourceFileWriter(
        ILogger<ResourceFileWriter> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
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

        var parentFolder = Path.GetDirectoryName(resourcePath);
        if (!string.IsNullOrEmpty(parentFolder)
            && !Directory.Exists(parentFolder))
        {
            try
            {
                Directory.CreateDirectory(parentFolder);
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to create parent folder for resource: '{resource}'")
                    .WithException(ex);
            }
        }

        // Stage all in-flight temp files in <project>/celbridge/.temp/. Centralising
        // them keeps user-visible folders clean of orphans after a crash, and the
        // workspace wipes the folder on load to clear any stragglers from a prior
        // session. Both the celbridge folder and the .tmp extension are filtered
        // by ResourceMonitor, so no spurious watcher events fire for the
        // intermediate write.
        var tempFolder = Path.Combine(
            resourceRegistry.ProjectFolderPath,
            ProjectConstants.MetaDataFolder,
            ProjectConstants.TempFolder);
        try
        {
            Directory.CreateDirectory(tempFolder);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to create temp folder: '{tempFolder}'")
                .WithException(ex);
        }

        IOException? lastException = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await WriteAtomicAsync(resourcePath, tempFolder, bytes);
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

    // Writes bytes to a uniquely-named temp file inside the project's central
    // temp folder, then atomically replaces the destination via File.Move.
    // A unique filename per write prevents concurrent writers to the same
    // destination from clobbering each other's intermediate state.
    private static async Task WriteAtomicAsync(string resourcePath, string tempFolder, byte[] bytes)
    {
        var tempPath = Path.Combine(tempFolder, Guid.NewGuid().ToString("N") + ".tmp");

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
