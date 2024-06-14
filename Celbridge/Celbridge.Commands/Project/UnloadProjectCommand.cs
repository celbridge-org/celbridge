using Celbridge.BaseLibrary.Commands.Project;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface.Navigation;
using Celbridge.BaseLibrary.Workspace;

namespace Celbridge.Commands.Project;

public class UnloadProjectCommand : CommandBase, IUnloadProjectCommand
{
    private const string EmptyPageName = "EmptyPage";

    public override async Task<Result> ExecuteAsync()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        var workspaceWrapper = serviceProvider.GetRequiredService<IWorkspaceWrapper>();
        var navigationService = serviceProvider.GetRequiredService<INavigationService>();
        var projectDataService = serviceProvider.GetRequiredService<IProjectDataService>();

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
