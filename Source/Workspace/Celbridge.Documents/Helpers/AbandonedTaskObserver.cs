namespace Celbridge.Documents.Helpers;

/// <summary>
/// Observes the faults of tasks that are no longer awaited.
/// </summary>
public static class AbandonedTaskObserver
{
    /// <summary>
    /// Swallows the eventual fault of an abandoned task. Reading Exception marks it observed, so it does
    /// not surface as an unobserved task exception.
    /// </summary>
    public static void Observe(Task task)
    {
        _ = task.ContinueWith(
            static abandonedTask => { _ = abandonedTask.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }
}
