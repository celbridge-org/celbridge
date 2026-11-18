using System.Diagnostics;
using Celbridge.Automation;
using Celbridge.FileSystem;
using Celbridge.Logging;

namespace Celbridge.Python.Services;

public sealed class TemporaryEnvironments : ITemporaryEnvironments
{
    // A closed console's processes can hold their temporary environment's files for a moment, so the removal retries
    // for a while.
    private static readonly TimeSpan RemovalTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RemovalRetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly ILocalFileSystem _fileSystem;
    private readonly ILogger<TemporaryEnvironments> _logger;
    private readonly IAutomationService _automationService;
    private readonly CelbridgeWheelFolders _folders;

    public TemporaryEnvironments(
        ILocalFileSystem fileSystem,
        ILogger<TemporaryEnvironments> logger,
        IAutomationService automationService,
        CelbridgeWheelFolders folders)
    {
        _fileSystem = fileSystem;
        _logger = logger;
        _automationService = automationService;
        _folders = folders;
    }

    public async Task RemoveAsync(string folderPath)
    {
        var temporaryEnvironmentsPath = Path.GetFullPath(_folders.TemporaryEnvironmentsFolderPath);
        var fullFolderPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
        var parentPath = Path.GetDirectoryName(fullFolderPath);
        if (!string.Equals(parentPath, temporaryEnvironmentsPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Left '{Path}' in place because it is not one of uv's temporary environments", folderPath);
            return;
        }

        var deadline = DateTime.UtcNow + RemovalTimeout;
        while (true)
        {
            var infoResult = await _fileSystem.GetInfoAsync(fullFolderPath);
            if (infoResult.IsSuccess &&
                infoResult.Value.Kind == StorageItemKind.NotFound)
            {
                return;
            }

            var deleteResult = await _fileSystem.DeleteFolderAsync(fullFolderPath, recursive: true);
            if (deleteResult.IsSuccess)
            {
                _logger.LogDebug("Removed the temporary environment '{Path}'", fullFolderPath);
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                _logger.LogDebug("Failed to remove the temporary environment '{Path}': {Error}",
                    fullFolderPath,
                    deleteResult.FirstErrorMessage);
                return;
            }

            await Task.Delay(RemovalRetryDelay);
        }
    }

    public async Task RemoveOrphanedAsync()
    {
        try
        {
            if (IsAnotherInstanceRunning())
            {
                _logger.LogDebug("Left the temporary environments in the uv cache in place because another Celbridge instance is running");
                _automationService.LogEvent(new TemporaryEnvironmentsKeptEvent());
                return;
            }

            var enumerateResult = await _fileSystem.EnumerateAsync(_folders.TemporaryEnvironmentsFolderPath, "*", recursive: false);
            if (enumerateResult.IsFailure)
            {
                // The folder appears once uv has built its first environment.
                return;
            }

            var orphanedFolderPaths = enumerateResult.Value
                .Where(entry => entry.IsFolder)
                .Select(entry => entry.FullPath)
                .ToList();

            if (orphanedFolderPaths.Count == 0)
            {
                return;
            }

            var removalTimer = Stopwatch.StartNew();
            var removedCount = 0;
            foreach (var folderPath in orphanedFolderPaths)
            {
                var deleteResult = await _fileSystem.DeleteFolderAsync(folderPath, recursive: true);
                if (deleteResult.IsFailure)
                {
                    _logger.LogDebug("Failed to remove the temporary environment '{Path}': {Error}",
                        folderPath,
                        deleteResult.FirstErrorMessage);
                    continue;
                }

                removedCount++;
            }

            _logger.LogInformation("Removed {RemovedCount} of {Count} temporary environments left in the uv cache in {DurationMs}ms",
                removedCount,
                orphanedFolderPaths.Count,
                removalTimer.ElapsedMilliseconds);
            _automationService.LogEvent(new TemporaryEnvironmentsRemovedEvent(removedCount, orphanedFolderPaths.Count));
        }
        catch (Exception exception)
        {
            // The orphaned environments stay until a later launch, which costs disk and nothing else.
            _logger.LogWarning(exception, "Failed to remove the temporary environments left in the uv cache");
        }
    }

    private static bool IsAnotherInstanceRunning()
    {
        using var currentProcess = Process.GetCurrentProcess();
        var namesakes = Process.GetProcessesByName(currentProcess.ProcessName);
        try
        {
            return namesakes.Any(process => process.Id != currentProcess.Id);
        }
        finally
        {
            foreach (var process in namesakes)
            {
                process.Dispose();
            }
        }
    }
}
