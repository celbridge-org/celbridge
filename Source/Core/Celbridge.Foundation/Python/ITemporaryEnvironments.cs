namespace Celbridge.Python;

/// <summary>
/// Removes the temporary environments of REPLs from uv's cache. uv run builds one environment for each REPL, and
/// removes the environment when the REPL exits normally. The environment stays in the cache when the REPL's console
/// closes, and when the application quits or crashes while the REPL is running.
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
