namespace Celbridge.BaseLibrary.Tasks;

public interface ITaskGroup
{
    event EventHandler<TaskProgressEventArgs>? ProgressChanged;
    Task ExecuteAsync(CancellationToken cancellationToken);
    void AddTask(ITask task);
}