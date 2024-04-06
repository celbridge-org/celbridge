using Celbridge.BaseLibrary.Tasks;
using System.Collections.Concurrent;

namespace Celbridge.Services.Tasks;

public class SchedulerService : ISchedulerService
{
    private readonly ILoggingService _loggingService;

    private readonly ConcurrentQueue<ITaskGroup> _queue = new();
    private bool _isProcessing = false;

    public CancellationToken CancellationToken { get; } = new();

    public SchedulerService(ILoggingService loggingService)
    {
        _loggingService = loggingService;
    }

    public void ScheduleFunction(Func<Task> task)
    {
        var functionTask = new FunctionTask(task);
        ScheduleTask(functionTask);
    }

    public void ScheduleTask(ITask task)
    {
        var taskGroup = new SerialTaskGroup();
        taskGroup.AddTask(task);
        ScheduleTaskGroup(taskGroup);
    }

    public void ScheduleParallelTasks(IEnumerable<ITask> tasks)
    {
        var taskGroup = new ParallelTaskGroup();
        foreach (var task in tasks)
        {
            taskGroup.AddTask(task);
        }
        ScheduleTaskGroup(taskGroup);
    }

    public void ScheduleParallelFunctions(IEnumerable<Func<Task>> tasks)
    {
        var taskGroup = new ParallelTaskGroup();
        foreach (var task in tasks)
        {
            var functionTask = new FunctionTask(task);
            taskGroup.AddTask(functionTask);
        }
        ScheduleTaskGroup(taskGroup);
    }

    private void ScheduleTaskGroup(ITaskGroup taskGroup)
    {
        _queue.Enqueue(taskGroup);
        TryProcessTasks();
    }

    private void TryProcessTasks()
    {
        if (!_isProcessing)
        {
            _isProcessing = true;

            // Todo: We may need to ensure that this async task is executed on the main thread
            var _ = ProcessTasksAsync();
        }
    }

    private async Task ProcessTasksAsync()
    {
        while (_queue.TryDequeue(out var taskGroup))
        {
            try
            {
                await taskGroup.ExecuteAsync();
            }
            catch (Exception ex)
            {
                // Log the error and attempt to continue
                _loggingService.Error($"Failed to execute task group. {ex}");
            }
        }

        _isProcessing = false;
    }
}