namespace Celbridge.Projects.Services;

/// <summary>
/// Takes and releases the locks that stop two instances of the application opening one project at once. Each
/// project folder has one lock. The lock is a file in the project's data folder. An instance keeps the file open
/// with no sharing while the project is open. The operating system closes the file when the process exits, so a
/// crash never leaves a project locked.
/// </summary>
internal interface IProjectLockService
{
    /// <summary>
    /// Returns true if this instance has the lock for the folder of the project file at the given path.
    /// </summary>
    bool HoldsLock(string projectFilePath);

    /// <summary>
    /// Takes the lock for the folder of the project file at the given path, creating the project's data folder if
    /// needed. Returns true once this instance has the lock, including a lock this instance already had. Returns
    /// false if another instance has the lock. Fails if the lock can't be taken for any other reason.
    /// </summary>
    Task<Result<bool>> TryTakeLockAsync(string projectFilePath);

    /// <summary>
    /// Releases the lock for the folder of the project file at the given path, if this instance has the lock.
    /// </summary>
    void ReleaseLock(string projectFilePath);
}
