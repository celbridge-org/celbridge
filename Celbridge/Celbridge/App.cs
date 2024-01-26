namespace Celbridge;

public partial class App : Application
{
    public App()
    {
        var settingsService = new SettingsService(WeakReferenceMessenger.Default);
        ApplicationTheme theme = ApplicationTheme.Light;
        if (settingsService.EditorSettings is not null)
        {
            theme = settingsService.EditorSettings.ApplicationTheme;
        }

#if HAS_UNO
        var themeName = theme.ToString();
        Uno.UI.ApplicationHelper.RequestedCustomTheme = themeName;
#else
	        this.RequestedTheme = theme;
#endif
    }

    public Window? MainWindow { get; private set; }
    public IHost? Host { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var builder = this.CreateBuilder(args)
            .Configure(host => host
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
                    RegisterServices(services);
                })
            );
        MainWindow = builder.Window;

        Host = builder.Build();

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
            var messengerService = Host!.Services.GetRequiredService<IMessenger>();
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
            rootFrame.Navigate(typeof(Shell), args.Arguments);
        }
        // Ensure the current window is active
        MainWindow.Activate();
    }

    private void RegisterServices(IServiceCollection services)
    {
        IMessenger messengerService = WeakReferenceMessenger.Default;
        ISettingsService settingsService = new SettingsService(messengerService);
        IResourceTypeService resourceTypeService = new ResourceTypeService();
        IResourceService resourceService = new ResourceService(messengerService, resourceTypeService);
        IInspectorService inspectorService = new InspectorService(messengerService);
        ISaveDataService saveDataService = new SaveDataService(messengerService);
        IDocumentService documentService = new DocumentService(messengerService);
        IDialogService dialogService = new DialogService(messengerService);
        IProjectService projectService = new ProjectService(messengerService, settingsService, saveDataService, resourceService, documentService, dialogService, inspectorService);
        IChatService chatService = new ChatService(settingsService);
        IConsoleService consoleService = new ConsoleService(messengerService, chatService);

        services.AddSingleton(messengerService);
        services.AddSingleton(settingsService);
        services.AddSingleton(resourceTypeService);
        services.AddSingleton(resourceService);
        services.AddSingleton(saveDataService);
        services.AddSingleton(dialogService);
        services.AddSingleton(projectService);
        services.AddSingleton(consoleService);
        services.AddSingleton(inspectorService);
        services.AddSingleton(documentService);
        services.AddSingleton(chatService);
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