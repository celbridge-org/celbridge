namespace Celbridge.BaseLibrary.Tasks;

public interface ITask
{
    Task<Result> ExecuteAsync(CancellationToken cancellationToken);
}