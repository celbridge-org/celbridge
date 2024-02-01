namespace Celbridge.BaseLibrary.Console;

public interface IConsoleService
{
    Task<bool> Execute(string command);
}
