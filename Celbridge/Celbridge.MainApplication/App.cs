using Celbridge.CommonServices.UserInterface;
using Celbridge.CommonViews.Pages;
using Celbridge.Dependencies;
using Celbridge.Dependencies.Extensions;
using Uno.Toolkit.UI;
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

    private ExtensionLoader _extensionLoader = new();

    private LegacyAppHelper? _legacyApp = new();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Load Extensions
        _extensionLoader.LoadExtension("Celbridge.Console");

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
                    _legacyApp?.RegisterServices(services);

                    // Configure all services and loaded extensions
                    Guard.IsNotNull(_extensionLoader);
                    var extensions = _extensionLoader.LoadedExtensions.Values.ToList();
                    ServiceConfiguration.ConfigureServices(services, extensions);
                }))
            );
        MainWindow = builder.Window;

        Host = builder.Build();

        // Setup the globally available helper for using the dependency injection framework.
        BaseLibrary.Core.Services.Initialize(Host.Services);

        // Initialize the UI system
        CommonViews.ServiceConfiguration.Initialize();

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
            MainWindow.Title = localizer["ApplicationName.Text"];
        }

        // Initialize the user interface system
        // Using the concrete class here to avoid exposing a setter for Window in the interface.
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
        };

        if (rootFrame.Content == null)
        {
            rootFrame.Navigate(typeof(MainPage), args.Arguments);
        }

        MainWindow.Activate();
    }

    private void ApplyTheme()
    {
        // Note: Application.RequestedTheme may only be set during the application constructor.
        // https://platform.uno/docs/articles/features/working-with-themes.html?tabs=windows

        // Note: Uno provides the SystemThemeHelper to support changing the application theme at runtime, without needing a restart.
        // The last time I tried it though it only partially worked, dialogs would still use the previous theme.

        const string ThemeSettingKey = "EditorSettings.Theme";

        // Putting a breakpoint on this line in Visual Studio causes an intermittent exception.
        // Probably some sort of race condition between the debugger and the underlying settings service.
        var editorSettings = ApplicationData.Current.LocalSettings.Values;

        if (!editorSettings.ContainsKey(ThemeSettingKey))
        {
            // No theme was previously selected, so the application will use the current OS theme.
            // Update the stored theme setting so that the settings dialog displays the correct value.
            // The double quotes are needed here because we store all settings as Json values.
            var osTheme = SystemThemeHelper.GetCurrentOsTheme();
            editorSettings[ThemeSettingKey] = osTheme == ApplicationTheme.Light ? "\"Light\"" : "\"Dark\"";
        }

        var themeSetting = editorSettings[ThemeSettingKey] as string;
        Guard.IsNotNull(themeSetting);

        var theme = ApplicationTheme.Light;
        if (themeSetting.Contains("Dark"))
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