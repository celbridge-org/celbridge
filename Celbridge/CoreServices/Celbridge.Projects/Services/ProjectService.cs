using Celbridge.Foundation;
using Celbridge.Navigation;
using Celbridge.Settings;
using Celbridge.Workspace;
using Newtonsoft.Json.Linq;

using Path = System.IO.Path;

namespace Celbridge.Projects.Services;

public class ProjectService : IProjectService
{
    private const int RecentProjectsMax = 5;

    private readonly IEditorSettings _editorSettings;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly INavigationService _navigationService;

    private const string ProjectDataFileKey = "projectDataFile";

    private const string EmptyPageName = "EmptyPage";

    public IProject? CurrentProject { get; private set; }

    public ProjectService(
        IEditorSettings editorSettings,
        IWorkspaceWrapper workspaceWrapper,
        INavigationService navigationService)
    {
        _editorSettings = editorSettings;
        _workspaceWrapper = workspaceWrapper;
        _navigationService = navigationService;
    }

    public Result ValidateNewProjectConfig(NewProjectConfig config)
    {
        if (config is null)
        {
            return Result.Fail("New project config is null.");
        }

        if (string.IsNullOrWhiteSpace(config.ProjectFilePath))
        {
            return Result.Fail("Project file path is empty.");
        }

        var projectName = Path.GetFileName(config.ProjectFilePath);        
        if (!ResourceKey.IsValidSegment(projectName))
        {
            return Result.Fail($"Project name is not valid: '{projectName}'");
        }

        var extension = Path.GetExtension(projectName);
        if (extension != FileNameConstants.ProjectFileExtension)
        {
            return Result.Fail($"Project file extension is not valid: '{projectName}'");
        }

        return Result.Ok();
    }

    public async Task<Result> CreateProjectAsync(NewProjectConfig config)
    {
        try
        {
            var projectFilePath = config.ProjectFilePath;
            if (File.Exists(projectFilePath))
            {
                return Result.Fail($"Failed to create project file because the file already exists: '{projectFilePath}'");
            }

            var createResult = await Project.CreateProjectAsync(config.ProjectFilePath);
            if (createResult.IsFailure)
            {
                return Result.Fail($"Failed to create project: '{config.ProjectFilePath}'");
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"An exception occured when creating project: '{config.ProjectFilePath}'");
        }
    }

    public Result LoadProject(string projectFilePath)
    {
        try
        {
            var loadResult = Project.LoadProject(projectFilePath);
            if (loadResult.IsFailure)
            {
                var failure = Result.Fail($"Failed to load project: {projectFilePath}");
                failure.MergeErrors(loadResult);
                return failure;
            }

            // Both data files have successfully loaded, so we can now populate the member variables
            CurrentProject = loadResult.Value;

            // Update the recent projects list in editor settings
            var recentProjects = _editorSettings.RecentProjects;
            recentProjects.Remove(projectFilePath);
            recentProjects.Insert(0, projectFilePath);
            while (recentProjects.Count > RecentProjectsMax)
            {
                recentProjects.RemoveAt(recentProjects.Count - 1);
            }
            _editorSettings.RecentProjects = recentProjects;

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to load project database. {ex.Message}");
        }
    }

    public async Task<Result> UnloadProjectAsync()
    {
        if (CurrentProject is null)
        {
            // Unloading a project that is not loaded is a no-op
            return Result.Ok();
        }

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Failed to unload project data because no project is loaded");
        }

        // Todo: Notify the workspace that it is about to close.
        // The workspace may want to perform some operations (e.g. save changes) before we close it.

        // Force the Workspace page to unload by navigating to an empty page.
        _navigationService.NavigateToPage(EmptyPageName);

        // Wait until the workspace is fully unloaded
        while (_workspaceWrapper.IsWorkspacePageLoaded)
        {
            await Task.Delay(50);
        }

        var disposableProject = CurrentProject as IDisposable;
        Guard.IsNotNull(disposableProject);
        disposableProject.Dispose();
        CurrentProject = null;

        return Result.Ok();
    }
}
