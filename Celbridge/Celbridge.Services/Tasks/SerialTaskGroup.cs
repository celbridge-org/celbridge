using Celbridge.BaseLibrary.Tasks;

namespace Celbridge.Services.Tasks;

public class SerialTaskGroup : ITaskGroup
{
    private readonly List<ITask> _tasks = new List<ITask>();

    public event EventHandler<TaskProgressEventArgs>? ProgressChanged;
    public CancellationToken CancellationToken { get; } = new();

    public void AddTask(ITask task) => _tasks.Add(task);

    public async Task ExecuteAsync()
    {
        foreach (var task in _tasks)
        {
            if (CancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await task.ExecuteAsync(CancellationToken);
                ProgressChanged?.Invoke(this, new TaskProgressEventArgs(true, task));
            }
            catch
            {
                ProgressChanged?.Invoke(this, new TaskProgressEventArgs(false, task));
                throw;
            }
        }
    }
}