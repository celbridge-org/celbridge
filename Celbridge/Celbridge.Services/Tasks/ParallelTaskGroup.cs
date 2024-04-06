using Celbridge.BaseLibrary.Tasks;

namespace Celbridge.Services.Tasks;

public class ParallelTaskGroup : ITaskGroup
{
    private readonly List<ITask> _tasks = new List<ITask>();

    public event EventHandler<TaskProgressEventArgs>? ProgressChanged;
    public CancellationToken CancellationToken { get; } = new();

    public void AddTask(ITask task) => _tasks.Add(task);

    public async Task ExecuteAsync()
    {
        var tasksWithProgress = _tasks
            .Select(task => ExecuteTaskAsync(task, CancellationToken))
            .ToList();

        await Task.WhenAll(tasksWithProgress);
    }

    private async Task ExecuteTaskAsync(ITask task, CancellationToken cancellationToken)
    {
        try
        {
            await task.ExecuteAsync(cancellationToken);
            ProgressChanged?.Invoke(this, new TaskProgressEventArgs(true, task));
        }
        catch
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            ProgressChanged?.Invoke(this, new TaskProgressEventArgs(false, task));
        }
    }
}