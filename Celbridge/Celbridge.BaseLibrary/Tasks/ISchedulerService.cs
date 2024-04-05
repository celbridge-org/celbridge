namespace Celbridge.BaseLibrary.Tasks;

/// <summary>
/// A service for scheduling and executing tasks.
/// </summary>
public interface ISchedulerService
{
    /// <summary>
    /// Schedule a single task for execution.
    /// </summary>
    void ScheduleTask(ITask task);

    /// <summary>
    /// Schedule a list of tasks to be executed in serial order.
    /// </summary>
    void ScheduleSerialTasks(List<ITask> tasks);

    /// <summary>
    /// Schedule a list of tasks to be executed in parallel.
    /// </summary>
    void ScheduleParallelTasks(List<ITask> tasks);
}