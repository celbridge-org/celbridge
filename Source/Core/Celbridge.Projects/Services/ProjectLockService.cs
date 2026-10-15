using Celbridge.Logging;

namespace Celbridge.Projects.Services;

internal sealed class ProjectLockService : IProjectLockService
{
    private const string LockFileName = "instance.lock";

    private readonly ILogger<ProjectLockService> _logger;
    private readonly IProjectDataFolderService _projectDataFolderService;

    // The open lock files, by data folder path. A load takes the new project's lock before the current project
    // closes, so two locks are held for a moment.
    private readonly Dictionary<string, FileStream> _heldLocks = new(PathComparison.Comparer);

    public ProjectLockService(
        ILogger<ProjectLockService> logger,
        IProjectDataFolderService projectDataFolderService)
    {
        _logger = logger;
        _projectDataFolderService = projectDataFolderService;
    }

    public bool HoldsLock(string projectFilePath)
    {
        var dataFolderPath = _projectDataFolderService.GetDataFolderPath(projectFilePath);

        return _heldLocks.ContainsKey(dataFolderPath);
    }

    public async Task<Result<bool>> TryTakeLockAsync(string projectFilePath)
    {
        var projectFolderPath = ProjectFileHelper.GetProjectFolderPath(projectFilePath);

        var dataFolderResult = await _projectDataFolderService.EnsureDataFolderAsync(projectFilePath);
        if (dataFolderResult.IsFailure)
        {
            return Result<bool>.Fail($"Failed to take the lock for the project folder: '{projectFolderPath}'")
                .WithErrors(dataFolderResult);
        }

        var dataFolderPath = dataFolderResult.Value;
        if (_heldLocks.ContainsKey(dataFolderPath))
        {
            return true;
        }

        var lockFilePath = Path.Combine(dataFolderPath, LockFileName);
        try
        {
            // When two instances open the file at once, exactly one succeeds. On macOS, .NET implements
            // FileShare.None with an advisory lock that the kernel grants to one process.
            var lockFile = new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _heldLocks[dataFolderPath] = lockFile;
        }
        catch (IOException exception)
        {
            // The open fails with an IOException when another process has the file open.
            _logger.LogInformation($"Refused the lock for the project folder because another instance holds it: '{projectFolderPath}'. {exception.Message}");
            return false;
        }
        catch (Exception exception)
        {
            return Result<bool>.Fail($"Failed to take the lock for the project folder: '{projectFolderPath}'")
                .WithException(exception);
        }

        _logger.LogInformation($"Took the lock for the project folder: '{projectFolderPath}'");

        return true;
    }

    public void ReleaseLock(string projectFilePath)
    {
        var dataFolderPath = _projectDataFolderService.GetDataFolderPath(projectFilePath);
        if (!_heldLocks.Remove(dataFolderPath, out var lockFile))
        {
            return;
        }

        lockFile.Dispose();

        var projectFolderPath = ProjectFileHelper.GetProjectFolderPath(projectFilePath);
        _logger.LogInformation($"Released the lock for the project folder: '{projectFolderPath}'");
    }
}
