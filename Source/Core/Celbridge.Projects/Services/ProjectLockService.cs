using Celbridge.Logging;

namespace Celbridge.Projects.Services;

internal sealed class ProjectLockService : IProjectLockService
{
    private const string LockFileName = "instance.lock";

    // On Windows, opening a file that another process has open fails with a sharing violation.
    private const int SharingViolationHResult = unchecked((int)0x80070020);

    // On macOS and Linux, .NET implements FileShare.None with an advisory lock. While another process has the lock,
    // the open fails with EWOULDBLOCK, and .NET passes the errno through as the HResult. The errno's value differs
    // between macOS and Linux.
    private const int MacOSWouldBlockErrno = 35;
    private const int LinuxWouldBlockErrno = 11;

    // A process such as a virus scanner can have the lock file open for a moment. A refused open is therefore tried
    // once more after this delay.
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

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
        FileStream? lockFile;
        try
        {
            lockFile = TryOpenLockFile(lockFilePath);
            if (lockFile is null)
            {
                await Task.Delay(RetryDelay);
                lockFile = TryOpenLockFile(lockFilePath);
            }
        }
        catch (Exception exception)
        {
            return Result<bool>.Fail($"Failed to take the lock for the project folder: '{projectFolderPath}'")
                .WithException(exception);
        }

        if (lockFile is null)
        {
            _logger.LogInformation($"Refused the lock for the project folder because another instance holds it: '{projectFolderPath}'");
            return false;
        }

        _heldLocks[dataFolderPath] = lockFile;
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

    // Returns the open lock file, or null if another process has the file open. Throws for any other failure.
    private static FileStream? TryOpenLockFile(string lockFilePath)
    {
        try
        {
            // When two instances open the file at once, exactly one succeeds.
            return new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception) when (IsHeldByAnotherProcess(exception))
        {
            return null;
        }
    }

    private static bool IsHeldByAnotherProcess(IOException exception)
    {
        if (OperatingSystem.IsWindows())
        {
            return exception.HResult == SharingViolationHResult;
        }

        var wouldBlockErrno = OperatingSystem.IsMacOS() ? MacOSWouldBlockErrno : LinuxWouldBlockErrno;

        return exception.HResult == wouldBlockErrno;
    }
}
