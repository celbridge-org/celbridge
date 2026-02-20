using Celbridge.Navigation;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Internal record to track page registration info.
/// </summary>
internal record PageInfo(Type PageType, ApplicationPage AppPage);

/// <summary>
/// Service responsible for top-level application page navigation.
/// </summary>
public class NavigationService : INavigationService
{
    private readonly Logging.ILogger<NavigationService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IUserInterfaceService _userInterfaceService;

    private Func<Type, object?, Result>? _navigateHandler;

    private readonly Dictionary<string, PageInfo> _pages = new();

    public NavigationService(
        Logging.ILogger<NavigationService> logger,
        IMessengerService messengerService,
        IUserInterfaceService userInterfaceService)
    {
        _logger = logger;
        _messengerService = messengerService;
        _userInterfaceService = userInterfaceService;
    }

    /// <summary>
    /// Sets the handler that performs the actual page navigation.
    /// Called by MainPage after it has loaded.
    /// </summary>
    public void SetNavigateHandler(Func<Type, object?, Result> handler)
    {
        Guard.IsNull(_navigateHandler);
        Guard.IsNotNull(handler);
        _navigateHandler = handler;
    }

    public Result RegisterPage(string tag, Type pageType)
    {
        return RegisterPage(tag, pageType, ApplicationPage.None);
    }

    /// <summary>
    /// Registers a page with an ApplicationPage mapping for UI state tracking.
    /// </summary>
    public Result RegisterPage(string tag, Type pageType, ApplicationPage appPage)
    {
        if (_pages.ContainsKey(tag))
        {
            return Result.Fail($"Failed to register page tag '{tag}' because it is already registered.");
        }

        if (!pageType.IsAssignableTo(typeof(Page)))
        {
            return Result.Fail($"Failed to register page tag '{tag}' because the type '{pageType}' does not inherit from Page.");
        }

        _pages[tag] = new PageInfo(pageType, appPage);

        return Result.Ok();
    }

    public Result UnregisterPage(string tag)
    {
        if (!_pages.ContainsKey(tag))
        {
            return Result.Fail($"Failed to unregister page tag '{tag}' because it is not registered.");
        }

        _pages.Remove(tag);

        return Result.Ok();
    }

    public Result NavigateToPage(string tag, object? parameter = null)
    {
        Guard.IsNotNull(_navigateHandler);

        if (!_pages.TryGetValue(tag, out var pageInfo))
        {
            var errorMessage = $"Failed to navigate to page '{tag}' because it is not registered.";
            _logger.LogError(errorMessage);
            return Result.Fail(errorMessage);
        }

        // Update application page state (skip if None)
        if (pageInfo.AppPage != ApplicationPage.None)
        {
            SetActivePage(pageInfo.AppPage);
        }

        var navigateResult = _navigateHandler(pageInfo.PageType, parameter);
        if (navigateResult.IsFailure)
        {
            _logger.LogError(navigateResult.Error);
            return navigateResult;
        }

        // Clear the cleanup flag after successful navigation
        IsWorkspacePageCleanupPending = false;

        return Result.Ok();
    }

    private void SetActivePage(ApplicationPage page)
    {
        bool isWorkspacePage = page == ApplicationPage.Workspace;

        if (isWorkspacePage)
        {
            _messengerService.Send(new WorkspacePageActivatedMessage());
        }
        else
        {
            _messengerService.Send(new WorkspacePageDeactivatedMessage());
        }

        _userInterfaceService.SetActivePage(page);
    }

    public bool IsWorkspacePageCleanupPending { get; private set; }

    public void RequestWorkspacePageCleanup()
    {
        IsWorkspacePageCleanupPending = true;
    }
}
