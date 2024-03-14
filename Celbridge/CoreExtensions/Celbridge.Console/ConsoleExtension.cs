using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Extensions;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.Console.ViewModels;
using Celbridge.Console.Views;

namespace Celbridge.Console;

public class ConsoleExtension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {
        config.AddSingleton<IConsoleService, ConsoleService>();

        config.AddTransient<ConsolePanel>();
        config.AddTransient<ConsolePanelViewModel>();
    }

    public Result Initialize()
    {
        var userInterfaceService = ServiceLocator.ServiceProvider.GetRequiredService<IUserInterfaceService>();

        userInterfaceService.RegisterWorkspacePanelConfig(
            new WorkspacePanelConfig(WorkspacePanelType.ConsolePanel, typeof(ConsolePanel)));

        return Result.Ok();
    }
}
