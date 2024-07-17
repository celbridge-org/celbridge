using Celbridge.BaseLibrary.Clipboard;
using Celbridge.BaseLibrary.Extensions;
using Celbridge.BaseLibrary.Navigation;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.Workspace.Commands;
using Celbridge.Workspace.Services;
using Celbridge.Workspace.ViewModels;
using Celbridge.Workspace.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Workspace;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {
        //
        // Register services
        //
        config.AddTransient<WorkspacePageViewModel>();
        config.AddTransient<IWorkspaceDataService, WorkspaceDataService>();
        config.AddTransient<IWorkspaceService, WorkspaceService>();
        config.AddTransient<IClipboardService, ClipboardService>();

        //
        // Register commands
        //
        config.AddTransient<ISaveWorkspaceStateCommand, SaveWorkspaceStateCommand>();
    }

    public Result Initialize()
    {
        var navigationService = ServiceLocator.ServiceProvider.GetRequiredService<INavigationService>();

        navigationService.RegisterPage(nameof(WorkspacePage), typeof(WorkspacePage));

        return Result.Ok();
    }
}
