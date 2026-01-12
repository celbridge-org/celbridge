using Celbridge.Commands;
using Celbridge.Core;
using Celbridge.Navigation;
using Celbridge.Projects;
using Celbridge.UserInterface.Views;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services;

public class NavigationService : INavigationService
{
    private const string HomePageName = "HomePage";
    private const string SettingsPageName = "SettingsPage";
    private const string WorkspacePageName = "WorkspacePage";
    private const string CommunityPageName = "CommunityPage";

    private readonly Logging.ILogger<NavigationService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly MainMenuUtils _mainMenuUtils;

    private INavigationProvider? _navigationProvider;
    public INavigationProvider NavigationProvider => _navigationProvider!;

    private readonly Dictionary<string, Type> _pageTypes = new();

    public NavigationService(
        Logging.ILogger<NavigationService> logger,
        IMessengerService messengerService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        IUserInterfaceService userInterfaceService,
        MainMenuUtils mainMenuUtils)
    {
        _logger = logger;
        _messengerService = messengerService;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _userInterfaceService = userInterfaceService;
        _mainMenuUtils = mainMenuUtils;
    }

    // The navigation provider is implemented by the MainPage class. Pages have to be loaded to be used, so the provider
    // instance is not available until the Main Page has finished loading. This method is used to set this dependency.
    public void SetNavigationProvider(INavigationProvider navigationProvider)
    {
        Guard.IsNotNull(navigationProvider);
        Guard.IsNull(_navigationProvider);
        _navigationProvider = navigationProvider;
    }

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
        return NavigateToPage(pageName, string.Empty);
    }

    public Result NavigateToPage(string pageName, object parameter)
    {
        Guard.IsNotNull(_navigationProvider);

        // Resolve the page type by looking up the page name
        if (!_pageTypes.TryGetValue(pageName, out var pageType))
        {
            var errorMessage = $"Failed to navigage to content page '{pageName}' because it is not registered.";
            _logger.LogError(errorMessage);

            return Result.Fail(errorMessage);
        }

        // Navigate using the resolved page type
        var navigateResult = _navigationProvider.NavigateToPage(pageType, parameter);
        if (navigateResult.IsFailure)
        {
            _logger.LogError(navigateResult.Error);
        }

        return navigateResult;
    }

    public Result NavigateByTag(string tag)
    {
        switch (tag)
        {
            case NavigationConstants.HomeTag:
                SetActivePage(ApplicationPage.Home);
                return NavigateToPage(HomePageName);

            case NavigationConstants.NewProjectTag:
                _ = _mainMenuUtils.ShowNewProjectDialogAsync();
                return Result.Ok();

            case NavigationConstants.OpenProjectTag:
                _ = _mainMenuUtils.ShowOpenProjectDialogAsync();
                return Result.Ok();

            case NavigationConstants.ReloadProjectTag:
                _ = ReloadProjectAsync();
                return Result.Ok();

            case NavigationConstants.SettingsTag:
                SetActivePage(ApplicationPage.Settings);
                return NavigateToPage(SettingsPageName);

            case NavigationConstants.WorkspaceTag:
                SetActivePage(ApplicationPage.Workspace);
                return NavigateToPage(WorkspacePageName);

            case NavigationConstants.ExplorerTag:
                SetActivePage(ApplicationPage.Workspace);
                var explorerResult = NavigateToPage(WorkspacePageName);
                if (_workspaceWrapper.IsWorkspacePageLoaded)
                {
                    _workspaceWrapper.WorkspaceService.SetCurrentContextAreaUsage(ContextAreaUse.Explorer);
                }
                return explorerResult;

            case NavigationConstants.SearchTag:
                SetActivePage(ApplicationPage.Workspace);
                var searchResult = NavigateToPage(WorkspacePageName);
                if (_workspaceWrapper.IsWorkspacePageLoaded)
                {
                    _workspaceWrapper.WorkspaceService.SetCurrentContextAreaUsage(ContextAreaUse.Search);
                }
                return searchResult;

            case NavigationConstants.DebugTag:
                SetActivePage(ApplicationPage.Workspace);
                var debugResult = NavigateToPage(WorkspacePageName);
                if (_workspaceWrapper.IsWorkspacePageLoaded)
                {
                    _workspaceWrapper.WorkspaceService.SetCurrentContextAreaUsage(ContextAreaUse.Debug);
                }
                return debugResult;

            case NavigationConstants.RevisionControlTag:
                SetActivePage(ApplicationPage.Workspace);
                var revisionResult = NavigateToPage(WorkspacePageName);
                if (_workspaceWrapper.IsWorkspacePageLoaded)
                {
                    _workspaceWrapper.WorkspaceService.SetCurrentContextAreaUsage(ContextAreaUse.VersionControl);
                }
                return revisionResult;

            case NavigationConstants.CommunityTag:
                SetActivePage(ApplicationPage.Community);
                return NavigateToPage(CommunityPageName);

            default:
                _logger.LogError($"Unknown navigation tag: {tag}");
                return Result.Fail($"Unknown navigation tag: {tag}");
        }
    }

    private void SetActivePage(ApplicationPage page)
    {
        // Send workspace-specific messages for backward compatibility
        bool isWorkspacePage = page == ApplicationPage.Workspace;

        if (isWorkspacePage)
        {
            _messengerService.Send(new WorkspacePageActivatedMessage());
        }
        else
        {
            _messengerService.Send(new WorkspacePageDeactivatedMessage());
        }

        // Set the active page in the UserInterfaceService (sends ActivePageChangedMessage)
        _userInterfaceService.SetActivePage(page);
    }

    private async Task ReloadProjectAsync()
    {
        var projectService = ServiceLocator.AcquireService<IProjectService>();
        if (projectService.CurrentProject is not null)
        {
            // Change the Navigation Cache status of the active persistent pages to Disabled, to allow them to be destroyed.
            ClearPersistenceOfAllLoadedPages();

            string projectPath = projectService.CurrentProject.ProjectFilePath;

            // Close any loaded project.
            // This will fail if there's no project currently open, but we can just ignore that.
            await _commandService.ExecuteImmediate<IUnloadProjectCommand>();

            _commandService.Execute<ILoadProjectCommand>((command) =>
            {
                command.ProjectFilePath = projectPath;
            });
        }
    }

    public void ClearPersistenceOfAllLoadedPages()
    {
        PersistentPage.ClearPersistenceOfAllLoadedPages();
    }

    public void UnloadPersistantUnfocusedPages()
    {
        PersistentPage.UnloadPersistantUnfocusedPages(NavigationProvider.GetCurrentPageName());
    }
}
