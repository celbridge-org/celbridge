using Celbridge.BaseLibrary.Navigation;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.MainApplication.Extensions;
using Celbridge.MainApplication;
using Celbridge.Services.UserInterface;
using Celbridge.Views.Pages;
using Uno.UI;
using Celbridge.Services.Navigation;

namespace Celbridge;

public class App : Application
{
    protected Window? MainWindow { get; private set; }
    protected IHost? Host { get; private set; }

    private ExtensionLoader _extensionLoader = new();
    private LegacyAppHelper? _legacyApp = new();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Load Extensions
        _extensionLoader.LoadExtension("Celbridge.Workspace");
        _extensionLoader.LoadExtension("Celbridge.Console");
        _extensionLoader.LoadExtension("Celbridge.Status");
        _extensionLoader.LoadExtension("Celbridge.Project");
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
                    _legacyApp?.RegisterServices(services);

                    // Configure all services and loaded extensions
                    Guard.IsNotNull(_extensionLoader);
                    var extensions = _extensionLoader.LoadedExtensions.Values.ToList();
                    ServiceConfiguration.ConfigureServices(services, extensions);
                })
            );
        MainWindow = builder.Window;

        Host = builder.Build();

        // Setup the globally available helper for using the dependency injection framework.
        BaseLibrary.Core.ServiceLocator.Initialize(Host.Services);

        // Initialize the UI system
        Views.ServiceConfiguration.Initialize();

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

        //
        // Initialize the user interface and page navigation services
        // We use the concrete classes here to avoid exposing the Initialize() methods in the public interface.
        //

        var userInterfaceService = Host.Services.GetRequiredService<IUserInterfaceService>() as UserInterfaceService;
        Guard.IsNotNull(userInterfaceService);
        userInterfaceService.Initialize();

        var navigationService = Host.Services.GetRequiredService<INavigationService>() as NavigationService;
        Guard.IsNotNull(navigationService);
        navigationService.Initialize(MainWindow);

        _legacyApp?.Initialize(Host.Services, MainWindow);

        MainWindow.Closed += (s, e) =>
        {
            _legacyApp?.OnMainWindowClosed();
        };

        rootFrame.Loaded += (s, e) =>
        {
            _legacyApp?.OnFrameLoaded(rootFrame);

#if DEBUG
            MainWindow.EnableHotReload();
#endif
        };

        if (rootFrame.Content == null)
        {
            rootFrame.Navigate(typeof(MainPage), args.Arguments);
        }

        MainWindow.Activate();
    }
}
