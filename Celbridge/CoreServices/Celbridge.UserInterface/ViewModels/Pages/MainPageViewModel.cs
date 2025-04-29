using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.FilePicker;
using Celbridge.Navigation;
using Celbridge.Projects;
using Celbridge.Settings;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.ViewModels.Pages;

public partial class MainPageViewModel : ObservableObject, INavigationProvider
{
    public const string HomeTag = "Home";
    public const string NewProjectTag = "NewProject";
    public const string OpenProjectTag = "OpenProject";
    public const string SettingsTag = "Settings";

    private const string HomePageName = "HomePage";
    private const string SettingsPageName = "SettingsPage";

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

    public Result NavigateToPage(Type pageType)
    {
        // Pass the empty string to avoid making the parameter nullable.
        return NavigateToPage(pageType, string.Empty);
    }

    public Result NavigateToPage(Type pageType, object parameter)
    {
        return OnNavigate?.Invoke(pageType, parameter)!;
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

    public void SelectNavigationItem(string tag)
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
        }

        _logger.LogError($"Failed to navigate to item {tag}.");
    }

    private async Task NavigateToHomeAsync()
    {
        if (IsWorkspaceLoaded)
        {
            _commandService.Execute<IUnloadProjectCommand>();

            // Wait until the project is unloaded before navigating
            while (IsWorkspaceLoaded)
            {
                await Task.Delay(50);
            }
        }

        // Clear the previous project so we don't try to reload it next time the application starts
        _editorSettings.PreviousProject = string.Empty;
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

