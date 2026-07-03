using Celbridge.Dialog;
using Celbridge.FilePicker;
using Celbridge.Forms;
using Celbridge.Localization;
using Celbridge.Navigation;
using Celbridge.UserInterface.Commands;
using Celbridge.UserInterface.Platform;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.Services.Dialogs;
using Celbridge.UserInterface.Services.Forms;
using Celbridge.UserInterface.ViewModels.Controls;
using Celbridge.UserInterface.ViewModels.Forms;
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
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IIconService, IconService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IUserInterfaceService, UserInterfaceService>();
        services.AddSingleton<IWorkspaceWrapper, WorkspaceWrapper>();
        services.AddSingleton<IUndoService, UndoService>();
        services.AddSingleton<IKeyboardShortcutService, KeyboardShortcutService>();
        services.AddSingleton<IFormService, FormService>();
        services.AddSingleton<ISpotlightService, SpotlightService>();
        services.AddSingleton<ISpotlightRegistry, SpotlightRegistry>();
        services.AddSingleton<MainMenuUtils>();
        services.AddTransient<FormBuilder>();

        // LayoutManager is a single implementation that exposes two interfaces:
        // IWindowModeService (window mode) and ILayoutService (region visibility).
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
        services.AddTransient<IAlertCommand, AlertCommand>();
        services.AddTransient<IConfirmActionCommand, ConfirmActionCommand>();
        services.AddTransient<ISpotlightCommand, SpotlightCommand>();
        services.AddTransient<IShowLogsCommand, ShowLogsCommand>();

        //
        // Register view models
        //

        services.AddTransient<MainPageViewModel>();
        services.AddTransient<HomePageViewModel>();
        services.AddTransient<WorkshopSettingsViewModel>();
        services.AddTransient<TitleBarViewModel>();
        services.AddTransient<PageNavigationToolbarViewModel>();
        services.AddTransient<MainMenuViewModel>();
        services.AddTransient<AlertDialogViewModel>();
        services.AddTransient<ConfirmationDialogViewModel>();
        services.AddTransient<ProgressDialogViewModel>();
        services.AddTransient<NewProjectDialogViewModel>();
        services.AddTransient<InputTextDialogViewModel>();
        services.AddTransient<SecretInputDialogViewModel>();
        services.AddTransient<NewFileDialogViewModel>();
        services.AddTransient<ResourcePickerDialogViewModel>();
        services.AddTransient<StackPanelElement>();
        services.AddTransient<TextBoxElement>();
        services.AddTransient<TextBlockElement>();
        services.AddTransient<DropDownTextBoxElement>();
        services.AddTransient<ButtonElement>();
        services.AddTransient<ComboBoxElement>();
        services.AddTransient<InfoBarElement>();
        services.AddTransient<CheckBoxElement>();
    }

    public static void Initialize()
    {
        var navigationService = ServiceLocator.AcquireService<INavigationService>() as NavigationService;
        Guard.IsNotNull(navigationService);

        // Seed the built-in spotlight landmarks into the runtime registry.
        var spotlightRegistry = ServiceLocator.AcquireService<ISpotlightRegistry>();
        SpotlightLandmarks.Seed(spotlightRegistry);

        // EmptyPage is used as a temporary navigation target when unloading workspaces
        navigationService.RegisterPage("Empty", typeof(EmptyPage), ApplicationPage.None);
        
        // Register application pages
        navigationService.RegisterPage(
            NavigationConstants.HomeTag, 
            typeof(HomePage), 
            ApplicationPage.Home);
                
        navigationService.RegisterPage(
            NavigationConstants.CommunityTag, 
            typeof(CommunityPage), 
            ApplicationPage.Community);

        navigationService.RegisterPage(
            NavigationConstants.SettingsTag,
            typeof(SettingsPage),
            ApplicationPage.Settings);
    }
}
