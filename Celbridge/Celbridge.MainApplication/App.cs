using Celbridge.BaseLibrary.Messaging;
using Celbridge.BaseLibrary.Settings;
using Celbridge.Dependencies;
using Windows.Storage;

namespace Celbridge.MainApplication;

public partial class App : Application
{
    public App()
    {
        ApplyTheme();
    }

    public Window? MainWindow { get; private set; }
    public IHost? Host { get; private set; }

    private ExtensionAssemblyLoader? _extensionLoader;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        LoadExtensions();

        var builder = this.CreateBuilder(args)
            .Configure((host => host
#if DEBUG
                // Switch to Development environment when running in DEBUG
                .UseEnvironment(Environments.Development)
#endif
                .UseConfiguration(configure: configBuilder =>
                    configBuilder
                        .EmbeddedSource<App>()
                        .Section<AppConfig>()
                )
                // Enable localization (see appsettings.json for supported languages)
                .UseLocalization()
                .ConfigureServices((context, services) =>
                {
                    // Register legacy Celbridge services
                    RegisterLegacyServices(services);

                    // Configure all services and core extensions
                    Guard.IsNotNull(_extensionLoader);
                    var extensionAssemblies = _extensionLoader.LoadedAssemblies.Values.ToList();
                    ServiceConfiguration.Configure(services, extensionAssemblies);
                }))
            );
        MainWindow = builder.Window;

        Host = builder.Build();

        InitializeNewServices();

        LegacyServiceProvider.Services = Host.Services;
        LegacyServiceProvider.MainWindow = MainWindow;

        // Do not repeat app initialization when the Window already has content,
        // just ensure that the window is active
        if (MainWindow.Content is not Frame rootFrame)
        {
            // Create a Frame to act as the navigation context and navigate to the first page
            rootFrame = new Frame();

            // Place the frame in the current Window
            MainWindow.Content = rootFrame;

            var localizer = Host.Services.GetRequiredService<IStringLocalizer>();
            MainWindow.Title = localizer["ApplicationName.Text"];
        }

        MainWindow.Closed += (s, e) =>
        {
            var messengerService = Host!.Services.GetRequiredService<IMessengerService>();
            var message = new ApplicationClosingMessage();
            messengerService.Send(message);
        };

        rootFrame.Loaded += (s, e) =>
        {
            // XamlRoot is required for displaying content dialogs
            var dialogService = Host.Services.GetRequiredService<IDialogService>();
            Guard.IsNotNull(rootFrame.XamlRoot);

            dialogService.XamlRoot = rootFrame.XamlRoot;

            // Start monitoring for save requests
            var saveDataService = Host.Services.GetRequiredService<ISaveDataService>();
            _ = saveDataService.StartMonitoringAsync(0.25);
        };

        if (rootFrame.Content == null)
        {
            // When the navigation stack isn't restored navigate to the first page,
            // configuring the new page by passing required information as a navigation
            // parameter
            rootFrame.Navigate(typeof(Legacy.Views.Shell), args.Arguments);
        }
        // Ensure the current window is active
        MainWindow.Activate();
    }

    private void ApplyTheme()
    {
        // Application.RequestedTheme may only be set during the application constructor.
        // We store EditorSettings in a SettingContainer in ApplicationData.Current.LocalSettings.Containers.
        // ApplicationData.Current.LocalSettings.Containers may not be accessed during the application constructor.
        // Workaround: Store the user selected theme in the ApplicationData.Current.LocalSettings.Values, which
        // can be accessed in the application constructor.

        var editorSettings = ApplicationData.Current.LocalSettings.Values;
        if (editorSettings.TryGetValue("Theme", out var value))
        {
            var theme = ApplicationTheme.Light;
            var themeSetting = (string)value;
            if (themeSetting == nameof(ApplicationColorTheme.Dark))
            {
                theme = ApplicationTheme.Dark;
            }
#if HAS_UNO
            var themeName = theme.ToString();
            Uno.UI.ApplicationHelper.RequestedCustomTheme = themeName;
#else
            this.RequestedTheme = theme;
#endif
        }
    }

    private void LoadExtensions()
    {
        // Todo: Discover extension assemblies by scanning a folder or via config
        _extensionLoader = new ExtensionAssemblyLoader();
        _extensionLoader.LoadAssembly("Celbridge.Console");
        _extensionLoader.LoadAssembly("Celbridge.Shell");
    }

    private void InitializeNewServices()
    {
        // Initialize the service locator
        var serviceProvider = Host!.Services.GetRequiredService<IServiceProvider>();

        // Test new DI architecture
        var consoleService = serviceProvider.GetRequiredService<BaseLibrary.Console.IConsoleService>();
        consoleService.Execute("print");

        var shellView = serviceProvider.GetRequiredService<Shell.Views.ShellView>();
        Guard.IsNotNull(shellView);
    }

    private void RegisterLegacyServices(IServiceCollection services)
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
}