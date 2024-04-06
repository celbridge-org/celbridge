using Celbridge.BaseLibrary.Tasks;

namespace Celbridge.Services.Tasks;

public class FunctionTask : ITask
{
    private readonly Func<Task> _func;

    public FunctionTask(Func<Task> Func)
    {
        _func = Func;
    }

    public async Task<Result> ExecuteAsync(CancellationToken cancellationToken)
    {
        await _func.Invoke();
        return Result.Ok();
    }
}