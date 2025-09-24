using System;
using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.FilePicker;
using Celbridge.Navigation;
using Celbridge.Projects;
using Celbridge.Settings;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.UserInterface.ViewModels.Pages;

public partial class MainPageViewModel : ObservableObject, INavigationProvider
{
    public const string HomeTag = "Home";
    public const string NewProjectTag = "NewProject";
    public const string OpenProjectTag = "OpenProject";
    public const string SettingsTag = "Settings";
    public const string SearchTag = "Search";
    public const string ExplorerTag = "Explorer";
    public const string DebugTag = "Debug";
    public const string RevisionControlTag = "RevisionControl";

    private const string HomePageName = "HomePage";
    private const string SettingsPageName = "SettingsPage";
    private const string WorkspacePageName = "WorkspacePage";   // Centralise these?

    private readonly IMessengerService _messengerService;
    private readonly Logging.ILogger<MainPageViewModel> _logger;
    private readonly INavigationService _navigationService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ICommandService _commandService;
    private readonly IEditorSettings _editorSettings;
    private readonly IUndoService _undoService;
    private readonly MainMenuUtils _mainMenuUtils;

    public MainPageViewModel(
        Logging.ILogger<MainPageViewModel> logger,
        IMessengerService messengerService,
        INavigationService navigationService,
        ICommandService commandService,
        IEditorSettings editorSettings,
        IUndoService undoService,
        IWorkspaceWrapper workspaceWrapper,
        MainMenuUtils mainMenuUtils)
    {
        _logger = logger;
        _messengerService = messengerService;
        _navigationService = navigationService;
        _commandService = commandService;
        _editorSettings = editorSettings;
        _undoService = undoService;
        _workspaceWrapper = workspaceWrapper;
        _mainMenuUtils = mainMenuUtils;
    }

    public bool IsWorkspaceLoaded => _workspaceWrapper.IsWorkspacePageLoaded;

    public event Func<Type, object, Result>? OnNavigate;
    public event Func<string, Result>? SelectNavigationItem;

    public Result NavigateToPage(Type pageType)
    {
        // Pass the empty string to avoid making the parameter nullable.
        return NavigateToPage(pageType, string.Empty);
    }

    public Result NavigateToPage(Type pageType, object parameter)
    {
        return OnNavigate?.Invoke(pageType, parameter)!;
    }

    public Result SelectNavigationItemUI(string navItemName)
    {
        return SelectNavigationItem?.Invoke(navItemName);
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
            case HomeTag:
                _ = NavigateToHomeAsync();
                return;

            case NewProjectTag:
                _ = _mainMenuUtils.ShowNewProjectDialogAsync();
                return;

            case OpenProjectTag:
                _ = _mainMenuUtils.ShowOpenProjectDialogAsync();
                return;

            case SettingsTag:
                _navigationService.NavigateToPage(SettingsPageName);
                break;

            case ExplorerTag:
                _navigationService.NavigateToPage(WorkspacePageName);
                if (_workspaceWrapper.IsWorkspacePageLoaded)
                {
                    _workspaceWrapper.WorkspaceService.SetContextAreaUsage(IWorkspaceService.ContextAreaUse.Explorer);
                }
                break;

            case SearchTag:
                _navigationService.NavigateToPage(WorkspacePageName);
                if (_workspaceWrapper.IsWorkspacePageLoaded)
                {
                    _workspaceWrapper.WorkspaceService.SetContextAreaUsage(IWorkspaceService.ContextAreaUse.Search);
                }
                break;

            case DebugTag:
                _navigationService.NavigateToPage(WorkspacePageName);
                if (_workspaceWrapper.IsWorkspacePageLoaded)
                {
                    _workspaceWrapper.WorkspaceService.SetContextAreaUsage(IWorkspaceService.ContextAreaUse.Debug);
                }
                break;

            case RevisionControlTag:
                _navigationService.NavigateToPage(WorkspacePageName);
                if (_workspaceWrapper.IsWorkspacePageLoaded)
                {
                    _workspaceWrapper.WorkspaceService.SetContextAreaUsage(IWorkspaceService.ContextAreaUse.RevisionControl);
                }
                break;
        }

        _logger.LogError($"Failed to navigate to item {tag}.");
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

