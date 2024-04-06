namespace Celbridge.BaseLibrary.Tasks;

public interface ITaskGroup
{
    event EventHandler<TaskProgressEventArgs>? ProgressChanged;
    CancellationToken CancellationToken { get; }
    Task ExecuteAsync();
    void AddTask(ITask task);
}