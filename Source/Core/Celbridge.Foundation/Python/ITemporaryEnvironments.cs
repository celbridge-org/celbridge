namespace Celbridge.Python;

/// <summary>
/// Removes the temporary environments that REPLs run in from uv's cache. uv run builds one environment for each
/// REPL, and removes the environment when the REPL exits normally. A REPL leaves its environment behind when its
/// console is torn down, or when a quit or a crash ends the REPL.
/// </summary>
public interface ITemporaryEnvironments
{
    /// <summary>
    /// Removes a REPL's temporary environment, once the processes that used the environment are gone. Only a folder
    /// directly inside uv's temporary environments folder is removed.
    /// </summary>
    Task RemoveAsync(string folderPath);

    /// <summary>
    /// Removes the temporary environments left behind by earlier runs of the application. Call this method before
    /// any console can start, so that every environment in the cache is orphaned. While another instance of the
    /// application is running, the method removes nothing, because that instance's REPLs use the same cache.
    /// </summary>
    Task RemoveOrphanedAsync();
}
