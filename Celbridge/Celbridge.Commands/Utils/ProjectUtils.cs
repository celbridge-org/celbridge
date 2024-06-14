using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface.Navigation;
using Celbridge.BaseLibrary.Workspace;

namespace Celbridge.Commands.Utils;

public static class ProjectUtils
{
    private const string EmptyPageName = "EmptyPage";
    private const string WorkspacePageName = "WorkspacePage";

    public static async Task<Result> CreateProjectAsync(
        IProjectDataService projectDataService,
        NewProjectConfig newProjectConfig)
    {
        var createResult = await projectDataService.CreateProjectDataAsync(newProjectConfig);
        if (createResult.IsSuccess)
        {
            return Result.Ok();
        }

        return Result.Fail($"Failed to create new project. {createResult.Error}");
    }

    public static async Task<Result> LoadProjectAsync(
        IWorkspaceWrapper workspaceWrapper,
        INavigationService navigationService, 
        IProjectDataService projectDataService, 
        string projectPath)
    {
        var openResult = projectDataService.LoadProjectData(projectPath);
        if (openResult.IsFailure)
        {
            return Result.Fail($"Failed to open project '{projectPath}'. {openResult.Error}");
        }

        navigationService.NavigateToPage(WorkspacePageName);

        while (!workspaceWrapper.IsWorkspaceLoaded)
        {
            await Task.Delay(50);
        }

        return Result.Ok();
    }

    public static async Task<Result> UnloadProjectAsync(
        IWorkspaceWrapper workspaceWrapper,
        INavigationService navigationService,
        IProjectDataService projectDataService)
    {
        if (!workspaceWrapper.IsWorkspaceLoaded)
        {
            return Result.Fail("Failed to unload project data because no project is loaded");
        }

        // Todo: Notify the workspace that it is about to close.
        // The workspace may want to perform some operations (e.g. save changes) before we close it.

        // Force the Workspace page to unload by navigating to an empty page.
        navigationService.NavigateToPage(EmptyPageName);

        // Wait until the workspace is fully unloaded
        while (workspaceWrapper.IsWorkspaceLoaded)
        {
            await Task.Delay(50);
        }

        if (projectDataService.LoadedProjectData is null)
        {
            return Result.Fail("Failed to unload project data because no project is loaded");
        }

        return projectDataService.UnloadProjectData();
    }
}
