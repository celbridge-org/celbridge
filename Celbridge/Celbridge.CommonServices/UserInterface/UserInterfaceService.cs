using Celbridge.CommonServices.Messaging;

namespace Celbridge.CommonServices.UserInterface;

public class UserInterfaceService : IUserInterfaceService
{
    private ILoggingService _loggingService;
    private IMessengerService _messengerService;

    private Window? _mainWindow;
    public Window MainWindow => _mainWindow!;

    private INavigationProvider? _navigationProvider;
 
    private Dictionary<string, Type> _pageTypes = new();

    public UserInterfaceService(ILoggingService loggingService,
                                IMessengerService messengerService)
    {
        _loggingService = loggingService;
        _messengerService = messengerService;

        _messengerService.Register<NavigationProviderLoadedMessage>(this, OnNavigationProviderLoaded);
    }

    public void Initialize(Window mainWindow)
    {
        Guard.IsNotNull(mainWindow);

        _mainWindow = mainWindow;

#if WINDOWS
        // Broadcast a message whenever the main window acquires or loses focus (Windows only).
        _mainWindow.Activated += MainWindow_Activated;
#endif
    }

#if WINDOWS
    private void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
    {
        var activationState = e.WindowActivationState;

        if (activationState == WindowActivationState.Deactivated)
        {
            var message = new MainWindowDeactivated();
            _messengerService.Send(message);
        }
        else if (activationState == WindowActivationState.PointerActivated ||
                 activationState == WindowActivationState.CodeActivated)
        {
            var message = new MainWindowActivated();
            _messengerService.Send(message);
        }
    }
#endif

    public Result RegisterPage(string pageName, Type pageType)
    {
        if (_pageTypes.ContainsKey(pageName))
        {
            return Result.Fail($"Failed to register page name '{pageName}' because it is already registered."); 
        }

        if (!pageType.IsAssignableTo(typeof(Page)))
        {
            return Result.Fail($"Failed to register page name '{pageName}' because the type '{pageType}' does not inherit from Page.");
        }

        _pageTypes[pageName] = pageType;

        return Result.Ok();
    }

    public Result UnregisterPage(string pageName)
    {
        if (!_pageTypes.ContainsKey(pageName))
        {
            return Result.Fail($"Failed to unregister page name '{pageName}' because it is not registered.");
        }

        _pageTypes.Remove(pageName);

        return Result.Ok();
    }

    public Result NavigateToPage(string pageName)
    {
        Guard.IsNotNull(_navigationProvider);

        // Resolve the page type by looking up the page name
        if (!_pageTypes.TryGetValue(pageName, out var pageType))
        {
            var errorMessage = $"Failed to navigage to content page '{pageName}' because it is not registered.";
            _loggingService.Error(errorMessage);

            return Result.Fail(errorMessage);
        }

        // Navigate using the resolved page type
        var navigateResult = _navigationProvider.NavigateToPage(pageType);

        return navigateResult;
    }

    private void OnNavigationProviderLoaded(object recipient, NavigationProviderLoadedMessage message)
    {
        // The navigation provider is implemented by the MainPage class. Pages have to be loaded to be used, so the provider
        // instance is not available until the Main Page has finished loading. We acquire this dependency via a message to
        // avoid tighly coupling the UserInterfaceService to MainPage.
        _navigationProvider = message.NavigationProvider;
    }
}
