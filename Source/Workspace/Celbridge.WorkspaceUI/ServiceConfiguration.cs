using Celbridge.DataTransfer;
using Celbridge.Navigation;
using Celbridge.UserInterface;
using Celbridge.WorkspaceUI.Commands;
using Celbridge.WorkspaceUI.Services;
using Celbridge.WorkspaceUI.ViewModels;
using Celbridge.WorkspaceUI.Views;

namespace Celbridge.WorkspaceUI;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        services.AddSingleton<IPanelFocusService, PanelFocusService>();
        services.AddTransient<IWorkspaceSettingsService, WorkspaceSettingsService>();
        services.AddTransient<IWorkspaceService, WorkspaceService>();
        services.AddTransient<IDataTransferService, DataTransferService>();
        services.AddTransient<WorkspaceLoader>();

        //
        // Register panels
        //

        services.AddTransient<IActivityPanel, ActivityPanel>();

        //
        // Register view models
        //

        services.AddTransient<WorkspacePageViewModel>();
        services.AddTransient<ActivityPanelViewModel>();

        //
        // Register commands
        //

        services.AddTransient<ICopyTextToClipboardCommand, CopyTextToClipboardCommand>();
        services.AddTransient<ICopyResourceToClipboardCommand, CopyResourceToClipboardCommand>();
        services.AddTransient<IPasteResourceFromClipboardCommand, PasteResourceFromClipboardCommand>();
        services.AddTransient<ISetRegionVisibilityCommand, SetRegionVisibilityCommand>();
        services.AddTransient<ISetConsoleMaximizedCommand, SetConsoleMaximizedCommand>();
        services.AddTransient<IResetPanelCommand, ResetPanelCommand>();
    }

    public static void Initialize()
    {
        var navigationService = ServiceLocator.AcquireService<INavigationService>() as UserInterface.Services.NavigationService;
        Guard.IsNotNull(navigationService);

        // Register the WorkspacePage with the NavigationService
        navigationService.RegisterPage(
            NavigationConstants.WorkspaceTag,
            typeof(WorkspacePage),
            ApplicationPage.Workspace);
    }
}
