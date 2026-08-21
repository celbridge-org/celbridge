using Celbridge.Dialog;
using Celbridge.FilePicker;
using Celbridge.Localization;
using Celbridge.UserInterface.Commands;
using Celbridge.UserInterface.DragDrop;
using Celbridge.UserInterface.Platform;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.Services.Dialogs;
using Celbridge.UserInterface.ViewModels.Controls;
using Celbridge.UserInterface.ViewModels.Dialogs;
using Celbridge.WebHost;
using Celbridge.UserInterface.ViewModels.Pages;
using Celbridge.UserInterface.Views;
using Celbridge.Workspace;

namespace Celbridge.UserInterface;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //
        services.AddSingleton<ILocalizerService, LocalizerService>();
        services.AddSingleton<IDialogFactory, DialogFactory>();
        services.AddSingleton<IIconService, IconService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IUserInterfaceService, UserInterfaceService>();
        services.AddSingleton<ILanguageService, LanguageService>();
        services.AddSingleton<IManagedFocus, ManagedFocus>();
        services.AddSingleton<IOverlayInputSuppressor, OverlayInputSuppressor>();
        services.AddSingleton<IHostWindowFocus, HostWindowFocus>();
        services.AddSingleton<IFocusReconciler, FocusReconciler>();
        services.AddSingleton<IWorkspaceWrapper, WorkspaceWrapper>();
        services.AddSingleton<IUndoService, UndoService>();
        services.AddSingleton<IKeyboardShortcutService, KeyboardShortcutService>();
        services.AddSingleton<ISpotlightService, SpotlightService>();
        services.AddSingleton<ISpotlightRegistry, SpotlightRegistry>();
        services.AddSingleton<IResourceDragCoordinator, ResourceDragCoordinator>();
        services.AddSingleton<IApplicationShell, ApplicationShell>();
        services.AddSingleton<MainMenuUtils>();

        // LayoutManager is a single implementation that exposes two interfaces:
        // IWindowModeService (window mode) and ILayoutService (surface visibility).
        services.AddSingleton<LayoutManager>();
        services.AddSingleton<IWindowModeService>(sp => sp.GetRequiredService<LayoutManager>());
        services.AddSingleton<ILayoutService>(sp => sp.GetRequiredService<LayoutManager>());

        // Window state management runs on both the packaged WinUI head and the Skia desktop head
        // via the cross-platform Microsoft.UI.Windowing APIs.
        services.AddSingleton<Helpers.WindowStateHelper>();

        PlatformServiceConfiguration.ConfigureServices(services);

        //
        // Register commands
        //

        services.AddTransient<ISetLayoutCommand, SetLayoutCommand>();
        services.AddTransient<ISetThemeCommand, SetThemeCommand>();
        services.AddTransient<ISetLanguageCommand, SetLanguageCommand>();
        services.AddTransient<IAlertCommand, AlertCommand>();
        services.AddTransient<IConfirmActionCommand, ConfirmActionCommand>();
        services.AddTransient<ISpotlightCommand, SpotlightCommand>();
        services.AddTransient<IShowLogsCommand, ShowLogsCommand>();
        services.AddTransient<IOpenBrowserCommand, OpenBrowserCommand>();

        //
        // Register view models
        //

        services.AddTransient<MainPageViewModel>();
        services.AddTransient<HomePageViewModel>();
        services.AddTransient<SettingsDialogViewModel>();
        services.AddTransient<WorkshopSettingsViewModel>();
        services.AddTransient<PrivacySettingsViewModel>();
        services.AddTransient<TitleBarViewModel>();
        services.AddTransient<ProjectSwitcherViewModel>();
        services.AddTransient<ApplicationMenuViewModel>();
        services.AddTransient<ViewMenuViewModel>();
        services.AddTransient<AlertDialogViewModel>();
        services.AddTransient<ConfirmationDialogViewModel>();
        services.AddTransient<ProgressDialogViewModel>();
        services.AddTransient<NewProjectDialogViewModel>();
        services.AddTransient<InputTextDialogViewModel>();
        services.AddTransient<SecretInputDialogViewModel>();
        services.AddTransient<NewFileDialogViewModel>();
        services.AddTransient<ResourcePickerDialogViewModel>();
    }

    public static void Initialize()
    {
        // Before anything looks a string up: the native user interface resolves its strings as it is built,
        // so the stored language has to be in force by then.
        var languageService = ServiceLocator.AcquireService<ILanguageService>();
        languageService.ApplyStoredLanguage();

        // Seed the built-in spotlight landmarks into the runtime registry.
        var spotlightRegistry = ServiceLocator.AcquireService<ISpotlightRegistry>();
        SpotlightLandmarks.Seed(spotlightRegistry);
    }
}
