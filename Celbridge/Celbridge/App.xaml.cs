using Celbridge.Commands;
using Celbridge.Commands.Services;
using Celbridge.Services;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.Views;
using Celbridge.Utilities;
using CommunityToolkit.Diagnostics;
using Uno.Resizetizer;

#if WINDOWS
using Celbridge.Settings;
using Windows.ApplicationModel.Activation;
#endif

using LaunchActivatedEventArgs = Microsoft.UI.Xaml.LaunchActivatedEventArgs;

namespace Celbridge;

public partial class App : Application
{
    public App()
    {
        this.InitializeComponent();
    }

    protected Window? MainWindow { get; private set; }
    protected IHost? Host { get; private set; }

    private ExtensionLoader _extensionLoader = new();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Catch all types of unhandled exceptions
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        this.UnhandledException += OnAppUnhandledException;

        // Load Extensions
        _extensionLoader.LoadExtension("Celbridge.Workspace");

        // Load WinUI Resources
        Resources.Build(r => r.Merged(
            new XamlControlsResources()));

        // Load custom resources
        Resources.Build(r => r.Merged(new ResourceDictionary
        {
            Source = new Uri("ms-appx:///Celbridge.UserInterface/Resources/Colors.xaml")
        }));

        Resources.Build(r => r.Merged(new ResourceDictionary
        {
            Source = new Uri("ms-appx:///Celbridge.UserInterface/Resources/FileIcons.xaml")
        }));

        // Load Uno.UI.Toolkit Resources
        Resources.Build(r => r.Merged(
            new ToolkitResources()));
        var builder = this.CreateBuilder(args)
            .Configure(host => host
#if DEBUG
                // Switch to Development environment when running in DEBUG
                .UseEnvironment(Environments.Development)
#endif
                /*
                 // We use NLog, but the built-in Uno logging can also be used for more detailed Uno & XAML logs if needed.
                .UseLogging(configure: (context, logBuilder) =>
                {
                    // Configure log levels for different categories of logging
                    logBuilder
                        .SetMinimumLevel(
                            context.HostingEnvironment.IsDevelopment() ?
                                LogLevel.Information :
                                LogLevel.Warning)

                        // Default filters for core Uno Platform namespaces
                        .CoreLogLevel(LogLevel.Warning);

                    // Uno Platform namespace filter groups
                    // Uncomment individual methods to see more detailed logging
                    //// Generic Xaml events
                    logBuilder.XamlLogLevel(LogLevel.Debug);
                    //// Layout specific messages
                    //logBuilder.XamlLayoutLogLevel(LogLevel.Debug);
                    //// Storage messages
                    //logBuilder.StorageLogLevel(LogLevel.Debug);
                    //// Binding related messages
                    //logBuilder.XamlBindingLogLevel(LogLevel.Debug);
                    //// Binder memory references tracking
                    //logBuilder.BinderMemoryReferenceLogLevel(LogLevel.Debug);
                    //// DevServer and HotReload related
                    //logBuilder.HotReloadCoreLogLevel(LogLevel.Information);

                }, enableUnoLogging: true)
                */
                .UseConfiguration(configure: configBuilder =>
                    configBuilder
                        .EmbeddedSource<App>()
                        .Section<AppConfig>()
                )
                // Enable localization (see appsettings.json for supported languages)
                .UseLocalization()
                .ConfigureServices((context, services) =>
                {
                    // Register the IDispatcher service to support running code on the UI thread.
                    // Note: When we add multi-window support, this will need to change to support multiple dispatchers.
                    services.AddSingleton<IDispatcher>(new Dispatcher(MainWindow!));

                    // Configure all services and loaded extensions
                    Guard.IsNotNull(_extensionLoader);
                    var extensions = _extensionLoader.LoadedExtensions.Values.ToList();
                    ServiceConfiguration.ConfigureServices(services, extensions);
                })
            );
        MainWindow = builder.Window;

#if DEBUG
        MainWindow.EnableHotReload();
#endif
        MainWindow.SetWindowIcon();

        Host = builder.Build();

        // Setup the globally available helper for using the dependency injection framework.
        Core.ServiceLocator.Initialize(Host.Services);

        var logger = Host.Services.GetRequiredService<ILogger<App>>();
        var utilityService = Host.Services.GetRequiredService<IUtilityService>();
        var environmentInfo = utilityService.GetEnvironmentInfo();
        logger.LogDebug(environmentInfo.ToString());

        // Check if the application was opened with a project file argument 
#if WINDOWS
        var activatedEventArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
        if (activatedEventArgs.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.File)
        {
            var fileArgs = activatedEventArgs.Data as IFileActivatedEventArgs;
            if (fileArgs != null && fileArgs.Files.Any())
            {
                var storageFile = fileArgs.Files.FirstOrDefault() as StorageFile;
                if (storageFile != null)
                {
                    var projectFile = storageFile.Path;
                    logger.LogDebug($"Launched with project file: {projectFile}");
                    if (File.Exists(projectFile))
                    {
                        var editorSettings = Host.Services.GetRequiredService<IEditorSettings>();
                        editorSettings.PreviousLoadedProject = projectFile;
                    }
                }
            }
        }
#endif

        // Start the telemetry service
        // Todo: Enable this once we have user opt-in UI for telemetry.
        // var telemetryService = Host.Services.GetRequiredService<ITelemetryService>();
        // telemetryService.Initialize();

        // Initialize the Core Services
        CoreServices.ServiceConfiguration.Initialize();

        // Initialize any loaded extensions
        _extensionLoader.InitializeExtensions();

        // Do not repeat app initialization when the Window already has content,
        // just ensure that the window is active
        if (MainWindow.Content is not Frame rootFrame)
        {
            // Create a Frame to act as the navigation context and navigate to the first page
            rootFrame = new Frame();

            // Place the frame in the current Window
            MainWindow.Content = rootFrame;

            // Todo: Is this required in latest Uno?
            //var localizer = Host.Services.GetRequiredService<IStringLocalizer>();
            // MainWindow.Title = localizer["ApplicationName"];
        }

        MainWindow.Closed += (s, e) =>
        {
            // Todo: This doesn't get called on Skia+Gtk at all on exit.
            // Ideally on all platforms we would stop executing commands as soon as the application starts exiting.
            // This callback is supposedly implemented in CoreApplication already but it doesn't seem to work.
            // If we get reports of crashes on exit then this is where I would start looking.
            // https://github.com/unoplatform/uno/pull/10001/files
            var commandService = Host.Services.GetRequiredService<ICommandService>() as CommandService;
            Guard.IsNotNull(commandService);
            commandService.StopExecution();

            // Flush any events that are still pending in the logger
            var logger = Host.Services.GetRequiredService<Logging.ILogger<App>>();
            logger.Shutdown();
        };

        rootFrame.Loaded += (s, e) =>
        {
            //
            // Initialize the user interface and page navigation services
            // We use the concrete classes here to avoid exposing the Initialize() methods in the public interface.
            //

            var userInterfaceService = Host.Services.GetRequiredService<IUserInterfaceService>() as UserInterfaceService;
            Guard.IsNotNull(userInterfaceService);

            XamlRoot xamlRoot = rootFrame.XamlRoot!;
            Guard.IsNotNull(xamlRoot);

            userInterfaceService.Initialize(MainWindow, xamlRoot);

#if DEBUG
            MainWindow.EnableHotReload();
#endif

            // Start executing commands
            var commandService = Host.Services.GetRequiredService<ICommandService>() as CommandService;
            Guard.IsNotNull(commandService);
            commandService.StartExecution();
        };

        if (rootFrame.Content == null)
        {
            // When the navigation stack isn't restored navigate to the first page,
            // configuring the new page by passing required information as a navigation
            // parameter
            rootFrame.Navigate(typeof(MainPage), args.Arguments);
        }

        // Ensure the current window is active
        MainWindow.Activate();
    }

    private void OnUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        HandleException(e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        HandleException(e.Exception);
        // e.SetObserved(); // prevent the crash
    }

    private void OnAppUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        HandleException(e.Exception);
        // e.Handled = true; //  // prevent the crash
    }

    private void HandleException(Exception? exception)
    {
        if (Host is null)
        {
            return;
        }

        var logger = Host.Services.GetRequiredService<ILogger<App>>();
        logger.LogError(exception, "An unhandled exception occurred");
    }
}
