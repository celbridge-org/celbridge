using Celbridge.BaseLibrary.Tasks;

namespace Celbridge.Services.Tasks;

public class SerialTaskGroup : ITaskGroup
{
    private readonly List<ITask> _tasks = new List<ITask>();

    public event EventHandler<TaskProgressEventArgs>? ProgressChanged;

    public void AddTask(ITask task) => _tasks.Add(task);

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        foreach (var task in _tasks)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await task.ExecuteAsync(cancellationToken);
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