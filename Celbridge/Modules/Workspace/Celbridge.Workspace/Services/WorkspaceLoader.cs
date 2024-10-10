using Celbridge.Extensions;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Workspace.Services;

public class WorkspaceLoader
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IExtensionService _extensionService;

    public WorkspaceLoader(
        IWorkspaceWrapper workspaceWrapper,
        IExtensionService extensionService)
    {
        _workspaceWrapper = workspaceWrapper;
        _extensionService = extensionService;
    }

    public async Task<Result> LoadWorkspaceAsync()
    {
        var workspaceService = _workspaceWrapper.WorkspaceService;
        if (workspaceService is null)
        {
            return Result.Fail("Workspace service is not initialized");
        }

        var explorerService = workspaceService.ExplorerService;

        //
        // Acquire the workspace settings
        //
        var workspaceSettingsService = workspaceService.WorkspaceSettingsService;
        var acquireResult = await workspaceSettingsService.AcquireWorkspaceSettingsAsync();
        if (acquireResult.IsFailure)
        {
            return Result.Fail("Failed to acquire the workspace settings")
                .AddErrors(acquireResult);
        }

        var workspaceSettings = workspaceSettingsService.WorkspaceSettings;
        Guard.IsNotNull(workspaceSettings);

        //
        // Load the extensions for the workspace
        //
        try
        {
            // Todo: Get this extension list from the project config / scanning the project folder.
            var extensions = new List<string>() { "Celbridge.Markdown" };
            foreach (var extension in extensions)
            {
                var loadResult = _extensionService.LoadExtension(extension);
                if (loadResult.IsFailure)
                {
                    var unloadResult = _extensionService.UnloadExtensions();
                    if (unloadResult.IsFailure)
                    {
                        return Result.Fail($"Failed to unload extensions after loading extension failed: {extension}")
                            .AddErrors(loadResult)
                            .AddErrors(unloadResult);
                    }
                    return Result.Fail($"Failed to load extension: {extension}");
                }
            }
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"An exception occurred when loading extensions.");
        }

        //
        // Populate the resource registry.
        //
        try
        {
            // Restore previous state of expanded folders before populating resources
            var expandedFolders = await workspaceSettings.GetPropertyAsync<List<string>>("ExpandedFolders");
            if (expandedFolders is not null &&
                expandedFolders.Count > 0)
            {
                var resourceRegistry = workspaceService.ExplorerService.ResourceRegistry;
                foreach (var expandedFolder in expandedFolders)
                {
                    resourceRegistry.SetFolderIsExpanded(expandedFolder, true);
                }
            }

            var updateResult = await explorerService.UpdateResourcesAsync();
            if (updateResult.IsFailure)
            {
                return Result.Fail("Failed to update resources")
                    .AddErrors(updateResult);
            }
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"An exception occurred while populating the resource registry");
        }

        //
        // Restore the previous state of the workspace.
        // Any failures that occur here are logged as warnings and do not prevent the workspace from loading.
        //

        // Select the previous selected resource in the Explorer Panel.
        await explorerService.RestorePanelState();

        // Open previous opened documents in the Documents Panel
        var documentsService = workspaceService.DocumentsService;
        await documentsService.RestorePanelState();

        //
        // Update the current stored state of the workspace in preparation for the next session.
        //
        await explorerService.StoreSelectedResource();
        await documentsService.StoreSelectedDocument();
        await documentsService.StoreOpenDocuments();

        //
        // Initialize console scripting support
        //
        var consoleService = workspaceService.ConsoleService;
        var initResult = await consoleService.ConsolePanel.InitializeScripting();
        if (initResult.IsFailure)
        {
            return Result.Fail("Failed to initialize console scripting")
                .AddErrors(initResult);
        }

        return Result.Ok();
    }
}
