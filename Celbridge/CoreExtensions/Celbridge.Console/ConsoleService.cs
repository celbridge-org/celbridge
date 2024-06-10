using Celbridge.BaseLibrary.Console;
using Celbridge.Console.Views;

namespace Celbridge.Console;

public class ConsoleService : IConsoleService
{
    private readonly IServiceProvider _serviceProvider;

    public ConsoleService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public object CreateConsolePanel()
    {
        return _serviceProvider.GetRequiredService<ConsolePanel>();
    }

    public ICommandHistory CreateCommandHistory()
    {
        return new CommandHistory();
    }
}
