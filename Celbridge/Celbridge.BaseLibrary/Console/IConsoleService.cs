namespace Celbridge.BaseLibrary.Console;

public interface IConsoleService
{
    Task<Result> Execute(string command);

    string GetTestString();
}
