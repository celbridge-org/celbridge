using Celbridge.Commands;
using Celbridge.Console;
using Celbridge.Messaging;
using Celbridge.Navigation;
using Celbridge.Projects;
using Celbridge.Settings;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.UserInterface.ViewModels.Controls;

/// <summary>
/// ViewModel for the MainMenu control, handling project operations and application exit.
/// </summary>
public partial class MainMenuViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly INavigationService _navigationService;
    private readonly IEditorSettings _editorSettings;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly MainMenuUtils _mainMenuUtils;

    [ObservableProperty]
    private bool _isWorkspaceLoaded;

    public MainMenuViewModel(
        IMessengerService messengerService,
        ICommandService commandService,
        INavigationService navigationService,
        IEditorSettings editorSettings,
        IWorkspaceWrapper workspaceWrapper,
        MainMenuUtils mainMenuUtils)
    {
        _messengerService = messengerService;
        _commandService = commandService;
        _navigationService = navigationService;
        _editorSettings = editorSettings;
        _workspaceWrapper = workspaceWrapper;
        _mainMenuUtils = mainMenuUtils;
    }

    public void OnLoaded()
    {
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);
        _messengerService.Register<WorkspaceUnloadedMessage>(this, OnWorkspaceUnloaded);
        _messengerService.Register<ReloadProjectMessage>(this, OnReloadProject);

        IsWorkspaceLoaded = _workspaceWrapper.IsWorkspacePageLoaded;
    }

    public void OnUnloaded()
    {
        _messengerService.UnregisterAll(this);
    }

    private void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage message)
    {
        IsWorkspaceLoaded = true;
    }

    private void OnWorkspaceUnloaded(object recipient, WorkspaceUnloadedMessage message)
    {
        IsWorkspaceLoaded = false;
    }

    private void OnReloadProject(object recipient, ReloadProjectMessage message)
    {
        _ = ReloadProjectAsync();
    }

    public void NewProject()
    {
        _ = _mainMenuUtils.ShowNewProjectDialogAsync();
    }

    public void OpenProject()
    {
        _ = _mainMenuUtils.ShowOpenProjectDialogAsync();
    }

    public async Task ReloadProjectAsync()
    {
        var projectService = ServiceLocator.AcquireService<IProjectService>();
        if (projectService.CurrentProject is not null)
        {
            string projectPath = projectService.CurrentProject.ProjectFilePath;

            await _commandService.ExecuteImmediate<IUnloadProjectCommand>();

            _commandService.Execute<ILoadProjectCommand>((command) =>
            {
                command.ProjectFilePath = projectPath;
            });
        }
    }

    public async Task CloseProjectAsync()
    {
        var projectService = ServiceLocator.AcquireService<IProjectService>();
        if (projectService.CurrentProject is null)
        {
            return;
        }

        // Close the loaded project
        await _commandService.ExecuteImmediate<IUnloadProjectCommand>();

        // Clear the previous project setting so it won't auto-load next time
        _editorSettings.PreviousProject = string.Empty;

        // Navigate to home page
        _navigationService.NavigateToPage(NavigationConstants.HomeTag);
    }

    public void ExitApplication()
    {
        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        var mainWindow = userInterfaceService.MainWindow as Window;
        mainWindow?.Close();
    }
}
