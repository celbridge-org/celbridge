namespace Celbridge.FileSystem;

/// <summary>
/// Runs an async operation from a sync call site without deadlocking on a
/// captured UI synchronization context. Use only at the sync-to-async boundary;
/// each call pays a thread-pool hop.
/// </summary>
public static class SyncRunner
{
    public static T Run<T>(Func<Task<T>> asyncOperation)
    {
        return Task.Run(asyncOperation).GetAwaiter().GetResult();
    }

    public static void Run(Func<Task> asyncOperation)
    {
        Task.Run(asyncOperation).GetAwaiter().GetResult();
    }
}
