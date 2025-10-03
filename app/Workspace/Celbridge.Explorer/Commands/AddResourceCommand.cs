using Celbridge.Commands;
using Celbridge.Explorer.Services;
using Celbridge.Workspace;
using Celbridge.Documents;

namespace Celbridge.Explorer.Commands;

public class AddResourceCommand : CommandBase, IAddResourceCommand
{
    public override CommandFlags CommandFlags => CommandFlags.Undoable | CommandFlags.UpdateResources;

    public ResourceType ResourceType { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public ResourceKey DestResource { get; set; }

    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private string _addedResourcePath = string.Empty;

    private readonly ResourceArchiver _archiver;

    public AddResourceCommand(
        IServiceProvider serviceProvider,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;

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
                            return Result.Fail($"Failed to unarchive resource: {DestResource}")
                                .WithErrors(unarchiveResult);
                        }
                    }
                    else
                    {
                        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;

                        // This is a regular command execution, not a redo, so just create an empty file resource.
                        var createResult = documentsService.CreateDocumentResource(addedResourcePath);                        
                        if (createResult.IsFailure)
                        {
                            return Result.Fail($"Failed to create resource: {DestResource}")
                                .WithErrors(createResult);
                        }
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
            return Result.Fail($"An exception occurred when adding the resource.")
                .WithException(ex);
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

        // Open our document in the editor.
        OpenResourceDocument(DestResource);

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
                    return Result.Fail($"Failed to archive file resource: {DestResource}")
                        .WithErrors(archiveResult);
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
            return Result.Fail($"An exception occurred when undoing adding the resource.")
                .WithException(ex);
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    private static void OpenResourceDocument(ResourceKey resourceKey)
    {
        // %%% Need to wait for previous command to complete.
        //  NOTE : Been thinking about this a lot. I was wondering whether the AddResource command should automatically open the document, but I was thinking that in some cases
        //          we may have automations that add a great number of files to the project, and the user will probably not be amused at having to close all the millions of opened documents afterwards.
        //          We probably need to talk about this a little to get a decision on the best positioning for this.

        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspacePageLoaded)
        {
            throw new InvalidOperationException("Failed to add resource because workspace is not loaded");
        }

        var resourceRegistry = workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

        var commandService = ServiceLocator.AcquireService<ICommandService>();

        //
        //  Open our new file.
        //

        var filePath = resourceRegistry.GetResourcePath(resourceKey);
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
                commandService.Execute<IOpenDocumentCommand>(command =>
                {
                    command.FileResource = resourceKey;
                    command.ForceReload = false;
                });

                // Execute a command to select the welcome document
                commandService.Execute<ISelectDocumentCommand>(command =>
                {
                    command.FileResource = new ResourceKey(resourceKey);
                });
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public static async void AddFile(string sourcePath, ResourceKey destResource)
    {
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspacePageLoaded)
        {
            throw new InvalidOperationException("Failed to add resource because workspace is not loaded");
        }

        // If the destination resource is a existing folder, resolve the destination resource to a file in
        // that folder with the same name as the source file.
        var resourceRegistry = workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;
        var resolvedDestResource = resourceRegistry.ResolveSourcePathDestinationResource(sourcePath, destResource);

        var commandService = ServiceLocator.AcquireService<ICommandService>();

        await commandService.ExecuteAsync<IAddResourceCommand>(command =>
        {
            command.ResourceType = ResourceType.File;
            command.SourcePath = sourcePath;
            command.DestResource = resolvedDestResource;
        });

        OpenResourceDocument(resolvedDestResource);
    }

    public static void AddFile(ResourceKey destResource)
    {
        AddFile(new ResourceKey(), destResource);
    }

    public static void AddFolder(string sourcePath, ResourceKey destResource)
    {
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspacePageLoaded)
        {
            throw new InvalidOperationException("Failed to add resource because workspace is not loaded");
        }

        // If the destination resource is a existing folder, resolve the destination resource to a folder in
        // that folder with the same name as the source folder.
        var resourceRegistry = workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;
        var resolvedDestResource = resourceRegistry.ResolveSourcePathDestinationResource(sourcePath, destResource);

        var commandService = ServiceLocator.AcquireService<ICommandService>();

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
