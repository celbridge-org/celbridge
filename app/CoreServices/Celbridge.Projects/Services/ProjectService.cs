using Celbridge.Navigation;
using Celbridge.Settings;
using Celbridge.Workspace;
using Windows.Foundation;

using Path = System.IO.Path;

namespace Celbridge.Projects.Services;

public class ProjectService : IProjectService
{
    private const int RecentProjectsMax = 5;

    private readonly IEditorSettings _editorSettings;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly INavigationService _navigationService;

    private const string EmptyPageTag = "Empty";

    public event TypedEventHandler<IProjectService, IProjectService.RebuildShortcutsUIEventArgs>? RebuildShortcutsUI;

    public IProject? CurrentProject { get; private set; }

    public ProjectService(
        IEditorSettings editorSettings,
        INavigationService navigationService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _editorSettings = editorSettings;
        _navigationService = navigationService;
        _workspaceWrapper = workspaceWrapper;
    }

    public void RegisterRebuildShortcutsUI(TypedEventHandler<IProjectService, IProjectService.RebuildShortcutsUIEventArgs> handler)
    {
        RebuildShortcutsUI += handler;
    }

    public void UnregisterRebuildShortcutsUI(TypedEventHandler<IProjectService, IProjectService.RebuildShortcutsUIEventArgs> handler)
    {
        RebuildShortcutsUI -= handler;
    }

    public void InvokeRebuildShortcutsUI(NavigationBarSection navigationBarSection)
    {
        RebuildShortcutsUI?.Invoke(this, new IProjectService.RebuildShortcutsUIEventArgs() { NavigationBarSection = navigationBarSection });
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
        if (extension != ProjectConstants.ProjectFileExtension)
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

            var createResult = await Project.CreateProjectAsync(config.ProjectFilePath, config.ConfigType);
            if (createResult.IsFailure)
            {
                return Result.Fail($"Failed to create project: '{config.ProjectFilePath}'");
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occured when creating project: '{config.ProjectFilePath}'")
                .WithException(ex);
        }
    }

    public async Task<Result<IProject>> LoadProjectAsync(string projectFilePath)
    {
        try
        {
            var loadResult = await Project.LoadProjectAsync(projectFilePath);
            if (loadResult.IsFailure)
            {
                return Result<IProject>.Fail($"Failed to load project: {projectFilePath}")
                    .WithErrors(loadResult);
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

            return Result<IProject>.Ok(CurrentProject);
        }
        catch (Exception ex)
        {
            return Result<IProject>.Fail($"An exception occurred when loading the project database.")
                .WithException(ex);
        }
    }

    public async Task<Result> UnloadProjectAsync()
    {
        if (CurrentProject is null)
        {
            // Unloading a project that is not loaded is a no-op
            return Result.Ok();
        }

        // The logic here is quite complicated because we need to teardown the workspace in the
        // unloaded callback of the Workspace Page, and there are multiple code paths to consider.

        // The workspace page uses NavigationCacheMode.Required, so it stays loaded in memory
        // even when the user navigates to other pages like Home or Community.
        // We need to ensure proper cleanup of the workspace page.
        if (_workspaceWrapper.IsWorkspacePageLoaded)
        {
            // Navigate to the Workspace page first to make it the active/visible page.
            // This is necessary because OnNavigatingFrom (which handles cleanup) only runs
            // when navigating away from the currently visible page.
            var navResult = _navigationService.NavigateToPage(NavigationConstants.WorkspaceTag);
            if (navResult.IsFailure)
            {
                return Result.Fail("Failed to navigate to workspace page for cleanup")
                    .WithErrors(navResult);
            }

            // Give the UI enough time to complete the navigation and render.
            // This ensures the Workspace page is fully active before we navigate away.
            await Task.Delay(100);

            // Signal that the workspace page should perform cleanup when it unloads.
            // This must be called AFTER navigating to the Workspace page, because
            // NavigateToPage clears the cleanup flag after each navigation.
            _navigationService.RequestWorkspacePageCleanup();

            // Now navigate to the empty page to trigger OnNavigatingFrom on the WorkspacePage,
            // which will disable caching and allow proper cleanup on unload.
            navResult = _navigationService.NavigateToPage(EmptyPageTag);
            if (navResult.IsFailure)
            {
                return Result.Fail("Failed to navigate to empty page for workspace cleanup")
                    .WithErrors(navResult);
            }

            // Wait until the workspace is fully unloaded
            while (_workspaceWrapper.IsWorkspacePageLoaded)
            {
                await Task.Delay(50);
            }
        }

        var disposableProject = CurrentProject as IDisposable;
        Guard.IsNotNull(disposableProject);
        disposableProject.Dispose();
        CurrentProject = null;

        return Result.Ok();
    }
}
