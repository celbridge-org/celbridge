using Celbridge.Projects.Services;

namespace Celbridge.Tests.Projects;

/// <summary>
/// A project lock kept in memory. The fake records each take and release in Calls. HeldProjectFiles lists the
/// project files that have their lock taken.
/// </summary>
internal sealed class FakeProjectLockService : IProjectLockService
{
    public List<string> Calls { get; } = new();

    public List<string> HeldProjectFiles { get; } = new();

    public Result<bool> TakeResult { get; set; } = true;

    public bool HoldsLock(string projectFilePath)
    {
        return HeldProjectFiles.Contains(projectFilePath);
    }

    public Task<Result<bool>> TryTakeLockAsync(string projectFilePath)
    {
        Calls.Add($"take {projectFilePath}");
        if (TakeResult.IsSuccess && TakeResult.Value)
        {
            HeldProjectFiles.Add(projectFilePath);
        }

        return Task.FromResult(TakeResult);
    }

    public void ReleaseLock(string projectFilePath)
    {
        Calls.Add($"release {projectFilePath}");
        HeldProjectFiles.Remove(projectFilePath);
    }
}
