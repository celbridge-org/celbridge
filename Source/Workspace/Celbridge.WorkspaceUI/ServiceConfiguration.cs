using Celbridge.DataTransfer;
using Celbridge.UserInterface;
using Celbridge.Community;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Commands;
using Celbridge.WorkspaceUI.Platform;
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

        services.AddSingleton<IFocusService, FocusService>();
        services.AddSingleton<PanelFocusTracker>();
        services.AddSingleton<ICommunityService, CommunityService>();

        PlatformServiceConfiguration.ConfigureServices(services);

        services.AddTransient<IWorkspaceSettingsService, WorkspaceSettingsService>();
        services.AddTransient<IBindableWorkspaceSettings, BindableWorkspaceSettings>();
        services.AddTransient<IWorkspaceService, WorkspaceService>();

        // The application shell creates one view per project and tears it down when the project unloads.
        services.AddTransient<IWorkspaceView, WorkspaceView>();
        services.AddTransient<IDataTransferService, DataTransferService>();
        services.AddTransient<WorkspaceLoader>();

        //
        // Register panels
        //

        services.AddTransient<IUtilityPanel, UtilityPanel>();
        services.AddTransient<WorkspaceToast>();

        //
        // Register view models
        //

        services.AddTransient<WorkspaceViewModel>();
        services.AddTransient<UtilityPanelViewModel>();
        services.AddTransient<WorkspaceToastViewModel>();

        //
        // Register commands
        //

        services.AddTransient<ICopyTextToClipboardCommand, CopyTextToClipboardCommand>();
        services.AddTransient<ICopyResourceToClipboardCommand, CopyResourceToClipboardCommand>();
        services.AddTransient<IPasteResourceFromClipboardCommand, PasteResourceFromClipboardCommand>();
        services.AddTransient<ISetAreaVisibilityCommand, SetAreaVisibilityCommand>();
        services.AddTransient<IResetAreaSizeCommand, ResetAreaSizeCommand>();
        services.AddTransient<ISetBottomAreaAlignmentCommand, SetBottomAreaAlignmentCommand>();
        services.AddTransient<IPerformEditCommand, PerformEditCommand>();
    }

    public static void Initialize()
    {
        // Track managed focus changes for the lifetime of the app. Reports are no-ops until a
        // workspace is active.
        var panelFocusTracker = ServiceLocator.AcquireService<PanelFocusTracker>();
        panelFocusTracker.Start();
    }
}
