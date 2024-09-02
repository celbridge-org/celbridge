using CommunityToolkit.Diagnostics;

namespace Celbridge.Workspace.Services;

public class WorkspaceLoader
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public WorkspaceLoader(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public async Task<Result> LoadWorkspaceAsync()
    {
        var workspaceService = _workspaceWrapper.WorkspaceService;
        if (workspaceService is null)
        {
            return Result.Fail("Workspace service is not initialized");
        }

        //
        // Acquire the workspace database
        //
        var workspaceDataService = workspaceService.WorkspaceDataService;
        var acquireResult = await workspaceDataService.AcquireWorkspaceDataAsync();
        if (acquireResult.IsFailure)
        {
            var failure = Result.Fail("Failed to acquire the workspace data");
            failure.MergeErrors(acquireResult);
            return failure;
        }

        var workspaceData = workspaceDataService.LoadedWorkspaceData;
        Guard.IsNotNull(workspaceData);

        //
        // Restore the Project Panel view state
        //
        try
        {
            // Set expanded folders
            var expandedFolders = await workspaceData.GetPropertyAsync<List<string>>("ExpandedFolders");
            if (expandedFolders is not null &&
                expandedFolders.Count > 0)
            {
                var resourceRegistry = workspaceService.ExplorerService.ResourceRegistry;
                foreach (var expandedFolder in expandedFolders)
                {
                    resourceRegistry.SetFolderIsExpanded(expandedFolder, true);
                }
            }
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"An exception occurred while restoring the Project Panel view state");
        }

        //
        // Update the resource registry.
        //
        try
        {
            var explorerService = workspaceService.ExplorerService;
            var updateResult = await explorerService.UpdateResourcesAsync();
            if (updateResult.IsFailure)
            {
                var failure = Result.Fail("Failed to update resources");
                failure.MergeErrors(updateResult);
                return failure;
            }
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"Failed to update the resource registry");
        }

        //
        // Open previously opened documents
        //
        var documentsService = workspaceService.DocumentsService;
        var openResult = await documentsService.OpenPreviousDocuments();
        if (openResult.IsFailure)
        {
            var failure = Result.Fail("Failed to open previous documents");
            failure.MergeErrors(openResult);
            return failure;
        }

        // Allow a little time for the opened documents to populate.
        // This also gives the user time to visually register the progress bar (in the case of a very fast load).
        await Task.Delay(400);

        return Result.Ok();
    }
}
