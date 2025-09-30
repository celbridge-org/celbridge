using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Dialog;
using Celbridge.Navigation;
using Celbridge.Settings;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

using Path = System.IO.Path;

namespace Celbridge.Projects.Commands;

public class LoadProjectCommand : CommandBase, ILoadProjectCommand
{
    private const string HomePageName = "HomePage";
    private const string WorkspacePageName = "WorkspacePage";

    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectService _projectService;
    private readonly INavigationService _navigationService;
    private readonly IEditorSettings _editorSettings;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public LoadProjectCommand(
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IProjectService projectService,
        INavigationService navigationService,
        IEditorSettings editorSettings,
        IDialogService dialogService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _projectService = projectService;
        _navigationService = navigationService;
        _editorSettings = editorSettings;
        _dialogService = dialogService;
        _workspaceWrapper = workspaceWrapper;
    }

    public string ProjectFilePath { get; set; } = string.Empty;

    public override async Task<Result> ExecuteAsync()
    {
        if (string.IsNullOrEmpty(ProjectFilePath))
        {
            return Result.Fail("Failed to load project because path is empty.");
        }

        if (_projectService.CurrentProject?.ProjectFilePath == ProjectFilePath)
        {
            // The project is already loaded.
            // We can just early out here as we're already in the expected end state.
            return Result.Ok();
        }

        // Change the Navigation Cache status of the active persistent pages to Disabled, to allow them to be destroyed.
        _navigationService.ClearPersistenceOfAllLoadedPages();

        // Close any loaded project.
        // This will fail if there's no project currently open, but we can just ignore that.
        await _commandService.ExecuteImmediate<IUnloadProjectCommand>();

        // Load the project
        var loadResult = await LoadProjectAsync(ProjectFilePath);

        if (loadResult.IsFailure)
        {
            _editorSettings.PreviousProject = string.Empty;

            var titleString = _stringLocalizer.GetString("LoadProjectFailedAlert_Title");
            var messageString = _stringLocalizer.GetString("LoadProjectFailedAlert_Message", ProjectFilePath);

            await _dialogService.ShowAlertDialogAsync(titleString, messageString);

            // Return to the home page so the user can decide what to do next
            _navigationService.NavigateToPage(HomePageName);

            return Result.Fail($"Failed to load project: '{ProjectFilePath}'")
                .WithErrors(loadResult);
        }

        _editorSettings.PreviousProject = ProjectFilePath;

        // Opening Welcome Markdown document on opening of project if the file exists and is accessible.
        var targetFilePath = new ResourceKey("readme.md");

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

        // %%% Currently we have a foible, where having differing cases in the filenames from the expected can make the case pass
        //  the spot checks for the file being available on Windows, but then fail to load as the Loading command seems to be case sensitive.
        var filePath = resourceRegistry.GetResourcePath(targetFilePath);
        if (!string.IsNullOrEmpty(filePath) &&
            File.Exists(filePath))
        {
            try
            {
                // Ensure the file is accessible.
                //  This would be done better using DocumentsService.CanAccessFile but DocumentsService isn't created until
                //  the explorer starts and we may be reaching here before then.
                var fileInfo = new FileInfo(filePath);
                using var stream = fileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.Read);

                // Execute a command to open the HTML document.
                _commandService.Execute<IOpenDocumentCommand>(command =>
                {
                    command.FileResource = targetFilePath;
                    command.ForceReload = false;
                });

                // Execute a command to select the welcome document
                _commandService.Execute<ISelectDocumentCommand>(command =>
                {
                    command.FileResource = new ResourceKey(targetFilePath);
                });
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return Result.Ok();
    }

    private async Task<Result> LoadProjectAsync(string projectFilePath)
    {
        var loadResult = _projectService.LoadProject(projectFilePath);
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to open project file '{projectFilePath}'")
                .WithErrors(loadResult);
        }

        var loadPageCancelationToken = new CancellationTokenSource();
        _navigationService.NavigateToPage(WorkspacePageName, loadPageCancelationToken);

        // Wait until the workspace page either loads or cancels loading due to an error
        while (!_workspaceWrapper.IsWorkspacePageLoaded &&
               !loadPageCancelationToken.IsCancellationRequested)
        {
            await Task.Delay(50);
        }

        if (loadPageCancelationToken.IsCancellationRequested)
        {
            return Result.Fail("Failed to open project because an error occured");
        }

        // Ensure our Navigation Pane is focused on Explorer to match the presentation of the panels.
        if (_workspaceWrapper.IsWorkspacePageLoaded)
        {
            _navigationService.NavigationProvider.SelectNavigationItemByNameUI("ExplorerNavigationItem");
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void LoadProject(string projectFilePath)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<ILoadProjectCommand>(command =>
        {
            command.ProjectFilePath = projectFilePath;
        });
    }
}
