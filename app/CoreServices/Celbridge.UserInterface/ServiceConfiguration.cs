using Celbridge.Dialog;
using Celbridge.FilePicker;
using Celbridge.Forms;
using Celbridge.Localization;
using Celbridge.Navigation;
using Celbridge.UserInterface.Commands;
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
        services.AddSingleton<ILayoutManager, LayoutManager>();
        services.AddSingleton<IWorkspaceWrapper, WorkspaceWrapper>();
        services.AddSingleton<IUndoService, UndoService>();
        services.AddSingleton<IFormService, FormService>();
        services.AddSingleton<MainMenuUtils>();
        services.AddTransient<FormBuilder>();

#if WINDOWS
        // Register WindowStateHelper for Windows platform only
        services.AddSingleton<Helpers.WindowStateHelper>();
#endif

        //
        // Register commands
        //

        services.AddTransient<ISetLayoutCommand, SetLayoutCommand>();
        services.AddTransient<ISetPanelVisibilityCommand, SetPanelVisibilityCommand>();

        //
        // Register view models
        //

        services.AddTransient<MainPageViewModel>();
        services.AddTransient<HomePageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<TitleBarViewModel>();
        services.AddTransient<AlertDialogViewModel>();
        services.AddTransient<ConfirmationDialogViewModel>();
        services.AddTransient<ProgressDialogViewModel>();
        services.AddTransient<NewProjectDialogViewModel>();
        services.AddTransient<InputTextDialogViewModel>();
        services.AddTransient<AddFileDialogViewModel>();
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
        var navigationService = ServiceLocator.AcquireService<INavigationService>();

        navigationService.RegisterPage(nameof(EmptyPage), typeof(EmptyPage));
        navigationService.RegisterPage(nameof(HomePage), typeof(HomePage));
        navigationService.RegisterPage(nameof(SettingsPage), typeof(SettingsPage));
        navigationService.RegisterPage(nameof(CommunityPage), typeof(CommunityPage));
    }
}
