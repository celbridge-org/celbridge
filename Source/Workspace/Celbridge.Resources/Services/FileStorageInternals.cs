using Celbridge.Logging;

namespace Celbridge.Resources.Services;

/// <summary>
/// Helpers shared by FileStorage and TrashService for the OS-level transient
/// failure modes both layers hit (sharing violations from AV / indexer / sync
/// clients, DOS read-only attribute).
/// </summary>
internal static class FileStorageInternals
{
    // 3 attempts at 50/100/150ms backoff catches the common short locks.
    public const int MaxAttempts = 3;
    public const int BaseRetryDelayMs = 50;

    /// <summary>
    /// Runs an async IO operation under the bounded-retry policy. shouldRetry
    /// filters which IOExceptions to retry (defaults to all); non-IO exceptions
    /// propagate immediately.
    /// </summary>
    public static async Task<Result<T>> RunWithRetryAsync<T>(
        ILogger logger,
        string operationLabel,
        string resourceLabel,
        string resourcePath,
        Func<Task<T>> operation,
        Func<IOException, bool>? shouldRetry = null)
        where T : notnull
    {
        IOException? lastException = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var value = await operation();
                if (attempt > 1)
                {
                    logger.LogWarning($"{operationLabel} succeeded for '{resourcePath}' on attempt {attempt} of {MaxAttempts} after transient IO failures");
                }
                return value;
            }
            catch (IOException ex) when (shouldRetry?.Invoke(ex) ?? true)
            {
                lastException = ex;
                if (attempt < MaxAttempts)
                {
                    var delay = BaseRetryDelayMs * attempt;
                    logger.LogWarning(ex, $"{operationLabel} attempt {attempt} failed for '{resourcePath}', retrying after {delay}ms");
                    await Task.Delay(delay);
                }
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to {operationLabel.ToLowerInvariant()} file: '{resourceLabel}'")
                    .WithException(ex);
            }
        }

        return Result.Fail($"Failed to {operationLabel.ToLowerInvariant()} file after {MaxAttempts} attempts: '{resourceLabel}'")
            .WithException(lastException!);
    }

    /// <summary>
    /// Sync counterpart of RunWithRetryAsync. Used by the trash service's
    /// move-to-trash path and the chokepoint's atomic-rename step.
    /// </summary>
    public static async Task RetryTransientIOAsync(ILogger logger, string operationLabel, string resourcePath, Action action)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                action();
                if (attempt > 1)
                {
                    logger.LogWarning($"{operationLabel} succeeded for '{resourcePath}' on attempt {attempt} of {MaxAttempts} after transient IO failures");
                }
                return;
            }
            catch (IOException ex) when (attempt < MaxAttempts)
            {
                var delay = BaseRetryDelayMs * attempt;
                logger.LogWarning(ex, $"{operationLabel} attempt {attempt} failed for '{resourcePath}', retrying after {delay}ms");
                await Task.Delay(delay);
            }
        }
    }

    /// <summary>
    /// Clears the DOS read-only attribute before a move or delete. The user's
    /// invocation of the operation overrides the attribute, matching OS
    /// Explorer's "delete read-only file?" behaviour. Best-effort.
    /// </summary>
    public static void ClearReadOnlyIfSet(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists
                && info.IsReadOnly)
            {
                info.IsReadOnly = false;
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// Recursive read-only clear for folder operations.
    /// </summary>
    public static void ClearReadOnlyRecursive(string folder)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                ClearReadOnlyIfSet(file);
            }
        }
        catch
        {
        }
    }
}
