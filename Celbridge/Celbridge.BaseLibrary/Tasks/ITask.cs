namespace Celbridge.BaseLibrary.Tasks;

public interface ITask
{
    Task ExecuteAsync(CancellationToken cancellationToken);
}