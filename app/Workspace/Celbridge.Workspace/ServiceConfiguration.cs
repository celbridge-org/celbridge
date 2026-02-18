using Celbridge.DataTransfer;
using Celbridge.Navigation;
using Celbridge.UserInterface;
using Celbridge.Workspace.Commands;
using Celbridge.Workspace.Services;
using Celbridge.Workspace.ViewModels;
using Celbridge.Workspace.Views;

namespace Celbridge.Workspace;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Configure workspace sub-projects
        //

        Activities.ServiceConfiguration.ConfigureServices(services);
        Console.ServiceConfiguration.ConfigureServices(services);
        Documents.ServiceConfiguration.ConfigureServices(services);
        Entities.ServiceConfiguration.ConfigureServices(services);
        Explorer.ServiceConfiguration.ConfigureServices(services);
        GenerativeAI.ServiceConfiguration.ConfigureServices(services);
        Inspector.ServiceConfiguration.ConfigureServices(services);
        Python.ServiceConfiguration.ConfigureServices(services);
        Resources.ServiceConfiguration.ConfigureServices(services);
        Search.ServiceConfiguration.ConfigureServices(services);

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

        // Register the WorkspacePage with the NaviagtionService
        navigationService.RegisterPage(
            NavigationConstants.WorkspaceTag,
            typeof(WorkspacePage),
            ApplicationPage.Workspace);
    }
}
