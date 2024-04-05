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

    public void ScheduleTask(ITask task)
    {
        var taskList = new List<ITask>
        {
            task
        };
        ScheduleSerialTasks(taskList);
    }

    public void ScheduleSerialTasks(List<ITask> tasks)
    {
        var taskGroup = new SerialTaskGroup();
        foreach (var task in tasks)
        {
            taskGroup.AddTask(task);
        }
        ScheduleTaskGroup(taskGroup);
    }

    public void ScheduleParallelTasks(List<ITask> tasks)
    {
        var taskGroup = new ParallelTaskGroup();
        foreach (var task in tasks)
        {
            taskGroup.AddTask(task);
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
            Task.Run(ProcessTasksAsync);
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