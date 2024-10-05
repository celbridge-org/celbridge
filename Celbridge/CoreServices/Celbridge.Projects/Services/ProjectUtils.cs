using Celbridge.Navigation;
using Celbridge.Workspace;

namespace Celbridge.Projects.Services;

public static class ProjectUtils
{
    private const string EmptyPageName = "EmptyPage";

    public static async Task<Result> UnloadProjectAsync(
        IWorkspaceWrapper workspaceWrapper,
        INavigationService navigationService,
        IProjectService projectService)
    {
        if (!workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Failed to unload project data because no project is loaded");
        }

        // Todo: Notify the workspace that it is about to close.
        // The workspace may want to perform some operations (e.g. save changes) before we close it.

        // Force the Workspace page to unload by navigating to an empty page.
        navigationService.NavigateToPage(EmptyPageName);

        // Wait until the workspace is fully unloaded
        while (workspaceWrapper.IsWorkspacePageLoaded)
        {
            await Task.Delay(50);
        }

        if (projectService.LoadedProject is null)
        {
            return Result.Fail("Failed to unload project data because no project is loaded");
        }

        return projectService.UnloadProject();
    }
}
