using Celbridge.Commands;
using Celbridge.Projects;
using Celbridge.Explorer.Services;
using Celbridge.Workspace;

namespace Celbridge.Explorer.Commands;

public class AddResourceCommand : CommandBase, IAddResourceCommand
{
    public override UndoStackName UndoStackName => UndoStackName.Explorer;
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public ResourceType ResourceType { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public ResourceKey DestResource { get; set; }

    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectService _projectService;

    private string _addedResourcePath = string.Empty;

    private readonly ResourceArchiver _archiver;

    public AddResourceCommand(
        IServiceProvider serviceProvider,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        IProjectService projectService)
    {
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _projectService = projectService;

        _archiver = serviceProvider.GetRequiredService<ResourceArchiver>();
    }

    public override async Task<Result> ExecuteAsync()
    {
        var addResult = await AddResourceAsync();
        if (addResult.IsSuccess)
        {
            _commandService.Execute<ISelectResourceCommand>(command =>
            {
                command.Resource = DestResource;
            });

            return Result.Ok();
        }

        return addResult;
    }

    public override async Task<Result> UndoAsync()
    {
        var undoResult = await UndoAddResourceAsync();

        // The user may have deliberately selected a resource since the add was executed, so it would be
        // surprising if their selection was changed when undoing the add, so we leave the selected resource as is.

        return undoResult;
    }

    private async Task<Result> AddResourceAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to add resource because workspace is not loaded");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ExplorerService.ResourceRegistry;

        //
        // Validate the resource key
        //

        if (DestResource.IsEmpty)
        {
            return Result.Fail("Failed to create resource. Resource key is empty");
        }

        if (!ResourceKey.IsValidKey(DestResource))
        {
            return Result.Fail($"Failed to create resource. Resource key '{DestResource}' is not valid.");
        }

        //
        // Create the resource on disk
        //

        try
        {
            var addedResourcePath = resourceRegistry.GetResourcePath(DestResource);

            // Fail if the parent folder for the new resource does not exist.
            // We could attempt to create any missing parent folders, but it would make the undo logic trickier.
            var parentFolderPath = Path.GetDirectoryName(addedResourcePath);
            if (!Directory.Exists(parentFolderPath))
            {
                return Result.Fail($"Failed to create resource. Parent folder does not exist: '{parentFolderPath}'");
            }

            // It's important to fail if the resource already exists, because undoing this command
            // deletes the resource, which could lead to unexpected data loss.
            if (ResourceType == ResourceType.File)
            {
                if (File.Exists(addedResourcePath))
                {
                    return Result.Fail($"A file already exists at '{addedResourcePath}'.");
                }

                if (string.IsNullOrEmpty(SourcePath))
                {
                    if (_archiver.ArchivedResourceType == ResourceType.File)
                    {
                        // This is a redo of previously undone add resource command, so restore the archived
                        // version of the file.
                        var unarchiveResult = await _archiver.UnarchiveResourceAsync();
                        if (unarchiveResult.IsFailure)
                        {
                            var failure = Result.Fail($"Failed to unarchive resource: {DestResource}");
                            failure.MergeErrors(unarchiveResult);
                            return failure;
                        }
                    }
                    else
                    {
                        // This is a regular command execution, not a redo, so just create an empty text file.
                        File.WriteAllText(addedResourcePath, string.Empty);
                    }
                }
                else
                {
                    if (File.Exists(SourcePath))
                    {
                        File.Copy(SourcePath, addedResourcePath);
                    }
                    else
                    {
                        return Result.Fail($"Failed to create resource. Source file '{SourcePath}' does not exist.");
                    }
                }
            }
            else if (ResourceType == ResourceType.Folder)
            {
                if (Directory.Exists(addedResourcePath))
                {
                    return Result.Fail($"A folder already exists at '{addedResourcePath}'.");
                }

                if (string.IsNullOrEmpty(SourcePath))
                {
                    Directory.CreateDirectory(addedResourcePath);
                }
                else
                {
                    if (Directory.Exists(SourcePath))
                    {
                        ResourceUtils.CopyFolder(SourcePath, addedResourcePath);
                    }
                    else
                    {
                        return Result.Fail($"Failed to create resource. Source folder '{SourcePath}' does not exist.");
                    }
                }
            }

            // Note the path of the added resource for undoing
            _addedResourcePath = addedResourcePath;
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"An exception occurred when adding the resource.");
        }

        //
        // Expand the folder containing the newly created resource
        //
        var parentFolderKey = DestResource.GetParent();
        if (!parentFolderKey.IsEmpty)
        {
            resourceRegistry.SetFolderIsExpanded(parentFolderKey, true);
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    private async Task<Result> UndoAddResourceAsync()
    {
        //
        // Delete the previously added resource
        //

        try
        {
            // Clear the cached resource path to clean up
            var addedResourcePath = _addedResourcePath;
            _addedResourcePath = string.Empty;

            if (ResourceType == ResourceType.File &&
                File.Exists(addedResourcePath))
            {
                // Archive the file instead of just deleting it.
                // This preserves any changes that the user made since adding the resource.
                var archiveResult = await _archiver.ArchiveResourceAsync(DestResource);
                if (archiveResult.IsFailure)
                {
                    var failure = Result.Fail($"Failed to archive file resource: {DestResource}");
                    failure.MergeErrors(archiveResult);
                    return failure;
                }
            }
            else if (ResourceType == ResourceType.Folder &&
                Directory.Exists(addedResourcePath))
            {
                Directory.Delete(addedResourcePath, true);
            }

        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"An exception occurred when undoing adding the resource.");
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void AddFile(string sourcePath, ResourceKey destResource)
    {
        var workspaceWrapper = ServiceLocator.ServiceProvider.GetRequiredService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspacePageLoaded)
        {
            throw new InvalidOperationException("Failed to add resource because workspace is not loaded");
        }

        // If the destination resource is a existing folder, resolve the destination resource to a file in
        // that folder with the same name as the source file.
        var resourceRegistry = workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;
        var resolvedDestResource = resourceRegistry.ResolveSourcePathDestinationResource(sourcePath, destResource);

        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IAddResourceCommand>(command =>
        {
            command.ResourceType = ResourceType.File;
            command.SourcePath = sourcePath;
            command.DestResource = resolvedDestResource;
        });
    }

    public static void AddFile(ResourceKey destResource)
    {
        AddFile(new ResourceKey(), destResource);
    }

    public static void AddFolder(string sourcePath, ResourceKey destResource)
    {
        var workspaceWrapper = ServiceLocator.ServiceProvider.GetRequiredService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspacePageLoaded)
        {
            throw new InvalidOperationException("Failed to add resource because workspace is not loaded");
        }

        // If the destination resource is a existing folder, resolve the destination resource to a folder in
        // that folder with the same name as the source folder.
        var resourceRegistry = workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;
        var resolvedDestResource = resourceRegistry.ResolveSourcePathDestinationResource(sourcePath, destResource);

        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IAddResourceCommand>(command =>
        {
            command.ResourceType = ResourceType.Folder;
            command.SourcePath = sourcePath;
            command.DestResource = resolvedDestResource;
        });
    }

    public static void AddFolder(ResourceKey destResource)
    {
        AddFolder(new ResourceKey(), destResource);
    }
}
