using Celbridge.BaseLibrary.Messaging;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.Services.UserInterface;

namespace Celbridge.MainApplication;

/// <summary>
/// Provides temporary support for legacy features that are being reworked.
/// We can remove this once all legacy functionality has been replaced.
/// </summary>
public class LegacyAppHelper
{
    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IResourceTypeService, ResourceTypeService>();
        services.AddSingleton<IResourceService, ResourceService>();
        services.AddSingleton<IInspectorService, InspectorService>();
        services.AddSingleton<ISaveDataService, SaveDataService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IDocumentService, DocumentService>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IChatService, ChatService>();
        services.AddSingleton<IConsoleService, ConsoleService>();

        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<ConsoleViewModel>();
        services.AddSingleton<ProjectViewModel>();
        services.AddSingleton<LeftNavigationBarViewModel>();
        services.AddSingleton<RightNavigationBarViewModel>();
        services.AddSingleton<StatusBarViewModel>();
        services.AddSingleton<DocumentsViewModel>();
        services.AddSingleton<InspectorViewModel>();
        services.AddSingleton<DetailViewModel>();
        services.AddSingleton<MainMenuViewModel>();
        services.AddTransient<NewProjectViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AddResourceViewModel>();
        services.AddTransient<ProgressDialogViewModel>();
        services.AddTransient<TextFileDocumentViewModel>();
        services.AddTransient<HTMLDocumentViewModel>();
        services.AddTransient<PropertyListViewModel>();
        services.AddTransient<NumberPropertyViewModel>();
        services.AddTransient<TextPropertyViewModel>();
        services.AddTransient<BooleanPropertyViewModel>();
        services.AddTransient<PathPropertyViewModel>();
        services.AddTransient<RecordPropertyViewModel>();
        services.AddTransient<LoadProjectTask>();
    }

    public void Initialize(IServiceProvider services, Window mainWindow)
    {
        LegacyServiceProvider.Services = services;
        LegacyServiceProvider.MainWindow = mainWindow;

        var navigationService = services.GetRequiredService<INavigationService>();
        navigationService.RegisterPage(nameof(Shell), typeof(Shell));
    }

    public void OnMainWindowClosed()
    {
        var messengerService = LegacyServiceProvider.Services!.GetRequiredService<IMessengerService>();
        var message = new ApplicationClosingMessage();
        messengerService.Send(message);
    }

    public void OnFrameLoaded(Frame rootFrame)
    {
        // XamlRoot is required for displaying content dialogs
        var dialogService = LegacyServiceProvider.Services!.GetRequiredService<IDialogService>();
        Guard.IsNotNull(rootFrame.XamlRoot);

        dialogService.XamlRoot = rootFrame.XamlRoot;

        // Start monitoring for save requests
        var saveDataService = LegacyServiceProvider.Services!.GetRequiredService<ISaveDataService>();
        _ = saveDataService.StartMonitoringAsync(0.25);
    }
}
