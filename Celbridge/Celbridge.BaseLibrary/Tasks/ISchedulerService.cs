namespace Celbridge.BaseLibrary.Tasks;

/// <summary>
/// A service for scheduling and executing tasks.
/// </summary>
public interface ISchedulerService
{
    /// <summary>
    /// Schedule a single task for execution in serial order.
    /// </summary>
    void ScheduleTask(ITask task);

    /// <summary>
    /// Schedule a single async function for execution in serial order.
    /// </summary>
    void ScheduleFunction(Func<Task> task);

    /// <summary>
    /// Schedule a list of tasks to be executed in parallel.
    /// </summary>
    void ScheduleParallelTasks(IEnumerable<ITask> tasks);

    /// <summary>
    /// Schedule a list of async functions to be executed in parallel.
    /// </summary>
    void ScheduleParallelFunctions(IEnumerable<Func<Task>> tasks);
}