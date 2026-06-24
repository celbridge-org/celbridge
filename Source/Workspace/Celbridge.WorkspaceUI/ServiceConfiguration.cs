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

        // The file clipboard is platform-specific: macOS writes file URLs to NSPasteboard (the WinRT
        // storage-item clipboard does not round-trip on the Skia head), other heads use the WinRT
        // clipboard. It is a singleton because the macOS implementation remembers the copy/move mode of
        // its own write across calls.
        if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<IFileClipboard, MacFileClipboard>();
        }
        else
        {
            services.AddSingleton<IFileClipboard, WinRtFileClipboard>();
        }

        services.AddTransient<IWorkspaceSettingsService, WorkspaceSettingsService>();
        services.AddTransient<IBindableWorkspaceSettings, BindableWorkspaceSettings>();
        services.AddTransient<IWorkspaceService, WorkspaceService>();
        services.AddTransient<IDataTransferService, DataTransferService>();
        services.AddTransient<WorkspaceLoader>();
        services.AddTransient<ProjectCheckReporter>();

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
