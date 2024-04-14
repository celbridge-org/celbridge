using Celbridge.BaseLibrary.Console;

namespace Celbridge.Console;

public class ConsoleService : IConsoleService
{
    public ConsoleService()
    {}

    public ICommandHistory CreateCommandHistory()
    {
        return new CommandHistory();
    }
}
