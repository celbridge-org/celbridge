using Celbridge.Commands;
using Celbridge.Navigation;
using Celbridge.Projects;
using Celbridge.Settings;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.ViewModels.Pages;

public partial class MainPageViewModel : ObservableObject, INavigationProvider
{
    private const string HomePageName = "HomePage";
    private const string SettingsPageName = "SettingsPage";
    private const string WorkspacePageName = "WorkspacePage";
    private const string CommunityPageName = "CommunityPage";

    private readonly IMessengerService _messengerService;
    private readonly Logging.ILogger<MainPageViewModel> _logger;
    private readonly INavigationService _navigationService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ICommandService _commandService;
    private readonly IEditorSettings _editorSettings;
    private readonly IUndoService _undoService;
    private readonly MainMenuUtils _mainMenuUtils;
    private readonly IProjectService _projectService;

    public MainPageViewModel(
        Logging.ILogger<MainPageViewModel> logger,
        IMessengerService messengerService,
        INavigationService navigationService,
        ICommandService commandService,
        IEditorSettings editorSettings,
        IUndoService undoService,
        IWorkspaceWrapper workspaceWrapper,
        MainMenuUtils mainMenuUtils,
        IProjectService projectService)
    {
        _logger = logger;
        _messengerService = messengerService;
        _navigationService = navigationService;
        _commandService = commandService;
        _editorSettings = editorSettings;
        _undoService = undoService;
        _workspaceWrapper = workspaceWrapper;
        _mainMenuUtils = mainMenuUtils;
        _projectService = projectService;
    }

    public bool IsWorkspaceLoaded => _workspaceWrapper.IsWorkspacePageLoaded;

    public event Func<Type, object, Result>? OnNavigate;

    // %%% Considering whether this is still required.
    public event Func<string, Result>? SelectNavigationItem;

    public delegate string ReturnCurrentPageDelegate();

    public ReturnCurrentPageDelegate ReturnCurrentPage;

    public Result NavigateToPage(Type pageType)
    {
        // Pass the empty string to avoid making the parameter nullable.
        return NavigateToPage(pageType, string.Empty);
    }

    public Result NavigateToPage(Type pageType, object parameter)
    {
        return OnNavigate?.Invoke(pageType, parameter)!;
    }

    public Result SelectNavigationItemByNavigationTag(string navigationTag)
    {
        OnSelectNavigationItem(navigationTag);
        return Result.Ok();
    }

    public string GetCurrentPageName()
    {
        return ReturnCurrentPage();
    }

    public void OnMainPage_Loaded()
    {
        _messengerService.Register<WorkspaceLoadedMessage>(this, (r, m) =>
        {
            OnPropertyChanged(nameof(IsWorkspaceLoaded));
        });

        _messengerService.Register<WorkspaceUnloadedMessage>(this, (r, m) =>
        {
            OnPropertyChanged(nameof(IsWorkspaceLoaded));
        });

        // Register this class as the navigation provider for the application
        var navigationService = _navigationService as NavigationService;
        Guard.IsNotNull(navigationService);
        navigationService.SetNavigationProvider(this);

        // Open the previous project if one was loaded last time we ran the application.
        var previousProjectFile = _editorSettings.PreviousProject;
        if (!string.IsNullOrEmpty(previousProjectFile) &&
            File.Exists(previousProjectFile))
        { 
            _commandService.Execute<ILoadProjectCommand>((command) =>
            {
                command.ProjectFilePath = previousProjectFile;
            });
        }
        else
        {
            // No previous project to load, so navigate to the home page
            _ = NavigateToHomeAsync();
        }
    }

    public void OnMainPage_Unloaded()
    { }

    public void OnSelectNavigationItem(string tag)
    {
        switch (tag)
        {
            case NavigationConstants.HomeTag:
                _ = NavigateToHomeAsync();
                return;

            case NavigationConstants.NewProjectTag:
                _ = _mainMenuUtils.ShowNewProjectDialogAsync();
                return;

            case NavigationConstants.OpenProjectTag:
                _ = _mainMenuUtils.ShowOpenProjectDialogAsync();
                return;

            case NavigationConstants.ReopenProjectTag:
                _ = ReopenProjectAsync();
                return;

            case NavigationConstants.SettingsTag:
                _navigationService.NavigateToPage(SettingsPageName);
                return;

            case NavigationConstants.ExplorerTag:
                _navigationService.NavigateToPage(WorkspacePageName);
                if (_workspaceWrapper.IsWorkspacePageLoaded)
                {
                    _workspaceWrapper.WorkspaceService.SetCurrentContextAreaUsage(ContextAreaUse.Explorer);
                }
                return;

            case NavigationConstants.SearchTag:
                _navigationService.NavigateToPage(WorkspacePageName);
                if (_workspaceWrapper.IsWorkspacePageLoaded)
                {
                    _workspaceWrapper.WorkspaceService.SetCurrentContextAreaUsage(ContextAreaUse.Search);
                }
                return;

            case NavigationConstants.DebugTag:
                _navigationService.NavigateToPage(WorkspacePageName);
                if (_workspaceWrapper.IsWorkspacePageLoaded)
                {
                    _workspaceWrapper.WorkspaceService.SetCurrentContextAreaUsage(ContextAreaUse.Debug);
                }
                return;

            case NavigationConstants.RevisionControlTag:
                _navigationService.NavigateToPage(WorkspacePageName);
                if (_workspaceWrapper.IsWorkspacePageLoaded)
                {
                    _workspaceWrapper.WorkspaceService.SetCurrentContextAreaUsage(ContextAreaUse.VersionControl);
                }
                return;

            case NavigationConstants.CommunityTag:
                _navigationService.NavigateToPage(CommunityPageName);
                return;
        }

        _logger.LogError($"Failed to navigate to item {tag}.");
    }

    private async Task ReopenProjectAsync()
    {
        if (_projectService.CurrentProject is not null)
        {
            // Change the Navigation Cache status of the active persistent pages to Disabled, to allow them to be destroyed.
            _navigationService.ClearPersistenceOfAllLoadedPages();

            string projectPath = _projectService.CurrentProject.ProjectFilePath;

            // Close any loaded project.
            // This will fail if there's no project currently open, but we can just ignore that.
            await _commandService.ExecuteImmediate<IUnloadProjectCommand>();

            _commandService.Execute<ILoadProjectCommand>((command) =>
            {
                command.ProjectFilePath = projectPath;
            });
        }
    }

    private async Task NavigateToHomeAsync()
    {
        _navigationService.NavigateToPage(HomePageName);
    }

    public void Undo()
    {
        _undoService.Undo();
    }

    public void Redo()
    {
        _undoService.Redo();
    }
}

