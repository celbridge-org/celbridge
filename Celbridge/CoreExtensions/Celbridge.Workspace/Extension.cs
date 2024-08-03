using Celbridge.Clipboard;
using Celbridge.Extensions;
using Celbridge.Navigation;
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
    }

    public Result Initialize()
    {
        var navigationService = ServiceLocator.ServiceProvider.GetRequiredService<INavigationService>();

        navigationService.RegisterPage(nameof(WorkspacePage), typeof(WorkspacePage));

        return Result.Ok();
    }
}
