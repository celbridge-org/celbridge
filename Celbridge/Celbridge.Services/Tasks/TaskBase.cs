using Celbridge.BaseLibrary.Tasks;

namespace Celbridge.Services.Tasks;

public abstract class TaskBase : ITask
{
    public abstract Task ExecuteAsync(CancellationToken cancellationToken);
}