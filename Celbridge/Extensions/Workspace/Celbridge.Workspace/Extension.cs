using Celbridge.DataTransfer;
using Celbridge.Extensions;
using Celbridge.Navigation;
using Celbridge.Workspace.Commands;
using Celbridge.Workspace.Services;
using Celbridge.Workspace.ViewModels;
using Celbridge.Workspace.Views;

namespace Celbridge.Workspace;

public class Extension : IExtension
{
    public void ConfigureServices(IExtensionServiceCollection config)
    {
        //
        // Register workspace sub-projects
        //

        Console.ServiceConfiguration.ConfigureServices(config);
        Documents.ServiceConfiguration.ConfigureServices(config);
        Explorer.ServiceConfiguration.ConfigureServices(config);
        Inspector.ServiceConfiguration.ConfigureServices(config);
        Scripting.ServiceConfiguration.ConfigureServices(config);
        Status.ServiceConfiguration.ConfigureServices(config);

        //
        // Register services
        //

        config.AddTransient<IWorkspaceSettingsService, WorkspaceSettingsService>();
        config.AddTransient<IWorkspaceService, WorkspaceService>();
        config.AddTransient<IDataTransferService, DataTransferService>();
        config.AddTransient<WorkspaceLoader>();

        //
        // Register view models
        //

        config.AddTransient<WorkspacePageViewModel>();

        //
        // Register commands
        //

        config.AddTransient<ICopyTextToClipboardCommand, CopyTextToClipboardCommand>();
        config.AddTransient<ICopyResourceToClipboardCommand, CopyResourceToClipboardCommand>();
        config.AddTransient<IPasteResourceFromClipboardCommand, PasteResourceFromClipboardCommand>();
        config.AddTransient<IToggleFocusModeCommand, ToggleFocusModeCommand>();
        config.AddTransient<IAlertCommand, AlertCommand>();
    }

    public Result Initialize()
    {
        var navigationService = ServiceLocator.ServiceProvider.GetRequiredService<INavigationService>();

        navigationService.RegisterPage(nameof(WorkspacePage), typeof(WorkspacePage));

        return Result.Ok();
    }
}
