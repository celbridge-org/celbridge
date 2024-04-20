using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Extensions;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.Console.ViewModels;
using Celbridge.Console.Views;

namespace Celbridge.Console;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {
        config.AddTransient<ConsolePanel>();
        config.AddTransient<ConsolePanelViewModel>();
        config.AddTransient<ConsoleTabItem>();
        config.AddTransient<ConsoleTabItemViewModel>();
        config.AddSingleton<IConsoleService, ConsoleService>();
    }

    public Result Initialize()
    {
        var userInterfaceService = ServiceLocator.ServiceProvider.GetRequiredService<IUserInterfaceService>();

        userInterfaceService.RegisterWorkspacePanelConfig(
            new WorkspacePanelConfig(WorkspacePanelType.ConsolePanel, typeof(ConsolePanel)));

        return Result.Ok();
    }
}
