namespace Celbridge.BaseLibrary.Tasks;

public class TaskProgressEventArgs : EventArgs
{
    public bool Succeeded { get; }
    public ITask Task { get; }

    public TaskProgressEventArgs(bool succeeded, ITask task)
    {
        Succeeded = succeeded;
        Task = task;
    }
}