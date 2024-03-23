namespace Celbridge.BaseLibrary.Console;

public interface IConsoleService
{
    Task<Result> ExecuteAsync(string command);

    string GetTestString();
}
