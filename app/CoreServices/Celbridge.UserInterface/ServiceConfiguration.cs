using Celbridge.Dialog;
using Celbridge.FilePicker;
using Celbridge.Forms;
using Celbridge.Localization;
using Celbridge.Navigation;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.Services.Dialogs;
using Celbridge.UserInterface.Services.Forms;
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
        services.AddSingleton<IFormService, FormService>();
        services.AddSingleton<MainMenuUtils>();
        services.AddTransient<FormBuilder>();

        //
        // Register view models
        //

        services.AddTransient<MainPageViewModel>();
        services.AddTransient<HomePageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<AlertDialogViewModel>();
        services.AddTransient<ConfirmationDialogViewModel>();
        services.AddTransient<ProgressDialogViewModel>();
        services.AddTransient<NewProjectDialogViewModel>();
        services.AddTransient<InputTextDialogViewModel>();
        services.AddTransient<StackPanelElement>();
        services.AddTransient<TextBoxElement>();
        services.AddTransient<TextBlockElement>();
        services.AddTransient<DropDownTextBoxElement>();
        services.AddTransient<ButtonElement>();
        services.AddTransient<ComboBoxElement>();
        services.AddTransient<InfoBarElement>();
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
