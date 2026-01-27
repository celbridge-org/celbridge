using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Workspace;

namespace Celbridge.Explorer.Commands;

public class AddResourceCommand : CommandBase, IAddResourceCommand
{
    public override CommandFlags CommandFlags => CommandFlags.ForceUpdateResources;

    public ResourceType ResourceType { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public ResourceKey DestResource { get; set; }
    public bool OpenAfterAdding { get; set; } = false;

    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IFileTemplateService _fileTemplateService;

    public AddResourceCommand(
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        IFileTemplateService fileTemplateService)
    {
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _fileTemplateService = fileTemplateService;
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

            if (OpenAfterAdding)
            {
                OpenResourceDocument(DestResource);
            }

            return Result.Ok();
        }

        return addResult;
    }

    private async Task<Result> AddResourceAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to add resource because workspace is not loaded");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ResourceRegistry;
        var fileOpService = workspaceService.FileOperationService;

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

        var addedResourcePath = resourceRegistry.GetResourcePath(DestResource);

        // Fail if the parent folder for the new resource does not exist.
        var parentFolderPath = Path.GetDirectoryName(addedResourcePath);
        if (!Directory.Exists(parentFolderPath))
        {
            return Result.Fail($"Failed to create resource. Parent folder does not exist: '{parentFolderPath}'");
        }

        if (ResourceType == ResourceType.File)
        {
            if (File.Exists(addedResourcePath))
            {
                return Result.Fail($"A file already exists at '{addedResourcePath}'.");
            }

            if (string.IsNullOrEmpty(SourcePath))
            {
                // Create a new empty file
                var content = _fileTemplateService.GetNewFileContent(addedResourcePath);
                var createResult = await fileOpService.CreateFileAsync(addedResourcePath, content);
                if (createResult.IsFailure)
                {
                    return Result.Fail($"Failed to create resource: {DestResource}")
                        .WithErrors(createResult);
                }
            }
            else
            {
                // Copy from source path
                if (!File.Exists(SourcePath))
                {
                    return Result.Fail($"Failed to create resource. Source file '{SourcePath}' does not exist.");
                }

                var copyResult = await fileOpService.CopyFileAsync(SourcePath, addedResourcePath);
                if (copyResult.IsFailure)
                {
                    return copyResult;
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
                // Create a new empty folder
                var createResult = await fileOpService.CreateFolderAsync(addedResourcePath);
                if (createResult.IsFailure)
                {
                    return Result.Fail($"Failed to create folder: {DestResource}")
                        .WithErrors(createResult);
                }
            }
            else
            {
                // Copy from source path
                if (!Directory.Exists(SourcePath))
                {
                    return Result.Fail($"Failed to create resource. Source folder '{SourcePath}' does not exist.");
                }

                var copyResult = await fileOpService.CopyFolderAsync(SourcePath, addedResourcePath);
                if (copyResult.IsFailure)
                {
                    return copyResult;
                }
            }
        }

        //
        // Expand the folder containing the newly created resource
        //
        var parentFolderKey = DestResource.GetParent();
        if (!parentFolderKey.IsEmpty)
        {
            resourceRegistry.SetFolderIsExpanded(parentFolderKey, true);
        }

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

        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceRegistry;

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
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceRegistry;
        var resolvedDestResource = resourceRegistry.ResolveSourcePathDestinationResource(sourcePath, destResource);

        var commandService = ServiceLocator.AcquireService<ICommandService>();

        await commandService.ExecuteAsync<IAddResourceCommand>(command =>
        {
            command.ResourceType = ResourceType.File;
            command.SourcePath = sourcePath;
            command.DestResource = resolvedDestResource;
            command.OpenAfterAdding = true;
        });
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
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceRegistry;
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
