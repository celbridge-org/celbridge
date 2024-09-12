using Celbridge.Commands.Services;
using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.MainApplication.Services;
using Celbridge.MainApplication;
using Celbridge.Telemetry;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.Views;
using Celbridge.UserInterface;
using Celbridge.Utilities;
using Uno.UI;

namespace Celbridge;

public class App : Application
{
    protected Window? MainWindow { get; private set; }
    protected IHost? Host { get; private set; }

    private ExtensionLoader _extensionLoader = new();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Catch all types of unhandled exceptions
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        this.UnhandledException += OnAppUnhandledException;

#if DEBUG && WINDOWS
        // Open a console window for logging output
        ConsoleHelper.AllocConsole();
#endif

        // Load Extensions
        _extensionLoader.LoadExtension("Celbridge.Scripting");
        _extensionLoader.LoadExtension("Celbridge.ScriptUtils");
        _extensionLoader.LoadExtension("Celbridge.Workspace");
        _extensionLoader.LoadExtension("Celbridge.Console");
        _extensionLoader.LoadExtension("Celbridge.Status");
        _extensionLoader.LoadExtension("Celbridge.Explorer");
        _extensionLoader.LoadExtension("Celbridge.Inspector");
        _extensionLoader.LoadExtension("Celbridge.Documents");

        var builder = this.CreateBuilder(args)
            .Configure(host => host
#if DEBUG
                // Switch to Development environment when running in DEBUG
                .UseEnvironment(Environments.Development)
#endif
                .UseConfiguration(configure: configBuilder =>
                    configBuilder
                        .EmbeddedSource<App>()
                        .Section<MainApplication.Config.AppConfig>()
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
                    MainApplication.ServiceConfiguration.ConfigureServices(services, extensions);
                })
            );

        MainWindow = builder.Window;

        Host = builder.Build();

        var logger = Host.Services.GetRequiredService<ILogger<App>>();
        var utilityService = Host.Services.GetRequiredService<IUtilityService>();
        var environmentInfo = utilityService.GetEnvironmentInfo();
        logger.LogInformation(environmentInfo.ToString());

        // Setup the globally available helper for using the dependency injection framework.
        Core.ServiceLocator.Initialize(Host.Services);

        // Start the telemetry service
        // Todo: Don't start this service unless the user has opted-in to telemetry.
        var telemetryService = Host.Services.GetRequiredService<ITelemetryService>();
        telemetryService.Initialize();

        // Initialize the UI system
        UserInterface.ServiceConfiguration.Initialize();

        // Tell the loaded extensions to initialize before the application starts.
        _extensionLoader.InitializeExtensions();

        // Do not repeat app initialization when the Window already has content,
        // just ensure that the window is active
        if (MainWindow.Content is not Frame rootFrame)
        {
            // Create a Frame to act as the navigation context and navigate to the first page
            rootFrame = new Frame();

            // Place the frame in the current Window
            MainWindow.Content = rootFrame;

            var localizer = Host.Services.GetRequiredService<IStringLocalizer>();
            MainWindow.Title = localizer["ApplicationName"];
        }

        MainWindow.Closed += (s, e) =>
        {
#if DEBUG && WINDOWS
            ConsoleHelper.FreeConsole();
#endif

            // Todo: This doesn't get called on Skia+Gtk at all on exit.
            // Ideally on all platforms we would stop executing commands as soon as the application starts exiting.
            // This callback is supposedly implemented in CoreApplication already but it doesn't seem to work.
            // If we get reports of crashes on exit then this is where I would start looking.
            // https://github.com/unoplatform/uno/pull/10001/files
            var commandService = Host.Services.GetRequiredService<ICommandService>() as CommandService;
            Guard.IsNotNull(commandService);
            commandService.StopExecution();

            // Flush any events that are still pending in the logger
            var logger = Host.Services.GetRequiredService<ILogger<App>>();
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
            rootFrame.Navigate(typeof(MainPage), args.Arguments);
        }

        MainWindow.Activate();
    }

    private void OnUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        HandleException(e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
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