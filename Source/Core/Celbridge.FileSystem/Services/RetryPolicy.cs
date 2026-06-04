namespace Celbridge.FileSystem.Services;

/// <summary>
/// Bounded-retry policy for the OS-level transient failure modes IO operations
/// hit on Windows (sharing violations from antivirus, search indexers, cloud
/// sync clients). Three attempts at 50/100/150ms backoff catches the common
/// short locks; non-IO exceptions propagate immediately.
/// </summary>
internal static class RetryPolicy
{
    public const int MaxAttempts = 3;
    public const int BaseRetryDelayMs = 50;

    public static async Task<Result<T>> RunAsync<T>(
        ILogger logger,
        string operationLabel,
        string path,
        Func<Task<T>> operation,
        Func<IOException, bool>? shouldRetry = null)
        where T : notnull
    {
        IOException? lastException = null;

        // ConfigureAwait(false) throughout so the gateway never captures the
        // calling synchronization context. Sync bridges over this layer must
        // not deadlock when the caller is on the UI thread.
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var value = await operation().ConfigureAwait(false);
                if (attempt > 1)
                {
                    logger.LogWarning($"{operationLabel} succeeded for '{path}' on attempt {attempt} of {MaxAttempts} after transient IO failures");
                }

                return value;
            }
            catch (IOException ex) when (shouldRetry?.Invoke(ex) ?? true)
            {
                lastException = ex;
                if (attempt < MaxAttempts)
                {
                    var delay = BaseRetryDelayMs * attempt;
                    logger.LogWarning(ex, $"{operationLabel} attempt {attempt} failed for '{path}', retrying after {delay}ms");
                    await Task.Delay(delay).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to {operationLabel.ToLowerInvariant()} file: '{path}'")
                    .WithException(ex);
            }
        }

        return Result.Fail($"Failed to {operationLabel.ToLowerInvariant()} file after {MaxAttempts} attempts: '{path}'")
            .WithException(lastException!);
    }
}
