using Celbridge.Commands;
using Celbridge.Explorer;
using Celbridge.Workspace;

namespace Celbridge.Resources.Helpers;

/// <summary>
/// Helper class that encapsulates the core logic for adding resources to the project.
/// </summary>
public class AddResourceHelper
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IFileTemplateService _fileTemplateService;
    private readonly ICommandService _commandService;

    public AddResourceHelper(
        IWorkspaceWrapper workspaceWrapper,
        IFileTemplateService fileTemplateService,
        ICommandService commandService)
    {
        _workspaceWrapper = workspaceWrapper;
        _fileTemplateService = fileTemplateService;
        _commandService = commandService;
    }

    /// <summary>
    /// Add a resource (file or folder) to the project.
    /// </summary>
    public async Task<Result> AddResourceAsync(
        ResourceType resourceType,
        string sourcePath,
        ResourceKey destResource)
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Failed to add resource because workspace is not loaded");
        }

        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;
        var resourceRegistry = resourceService.Registry;
        var resourceOpService = resourceService.OperationService;

        //
        // Validate the resource key
        //

        var validationResult = ValidateDestResource(destResource);
        if (validationResult.IsFailure)
        {
            return validationResult;
        }

        //
        // Create the resource on disk
        //

        var fileStorage = _workspaceWrapper.WorkspaceService.FileStorage;

        // Fail if the parent folder for a new file does not exist.
        // Folder creation is allowed to materialize missing intermediate
        // ancestors via the chokepoint's idempotent CreateFolderAsync.
        if (resourceType == ResourceType.File)
        {
            var parentKey = destResource.GetParent();
            if (!parentKey.IsEmpty)
            {
                var parentInfoResult = await fileStorage.GetInfoAsync(parentKey);
                if (parentInfoResult.IsFailure
                    || parentInfoResult.Value.Kind != StorageItemKind.Folder)
                {
                    return Result.Fail($"Failed to create resource. Parent folder does not exist: '{parentKey}'");
                }
            }
        }

        var createResult = resourceType == ResourceType.File
            ? await CreateFileAsync(sourcePath, destResource, resourceOpService, fileStorage)
            : await CreateFolderAsync(sourcePath, destResource, resourceOpService, fileStorage);

        if (createResult.IsFailure)
        {
            return createResult;
        }

        //
        // Expand the folder containing the newly created resource
        //

        ExpandParentFolder(destResource);

        return Result.Ok();
    }

    private static Result ValidateDestResource(ResourceKey destResource)
    {
        if (destResource.IsEmpty)
        {
            return Result.Fail("Failed to create resource. Resource key is empty");
        }

        if (!ResourceKey.IsValidKey(destResource))
        {
            return Result.Fail($"Failed to create resource. Resource key '{destResource}' is not valid.");
        }

        return Result.Ok();
    }

    private async Task<Result> CreateFileAsync(
        string sourcePath,
        ResourceKey destResource,
        IResourceOperationService opService,
        IFileStorage fileStorage)
    {
        var infoResult = await fileStorage.GetInfoAsync(destResource);
        if (infoResult.IsSuccess
            && infoResult.Value.Kind != StorageItemKind.NotFound)
        {
            return Result.Fail($"A resource already exists at '{destResource}'.");
        }

        if (string.IsNullOrEmpty(sourcePath))
        {
            // Create a new empty file. The template service still consumes a
            // path to discriminate by file extension; the chokepoint write
            // takes the resource key.
            var destPathResult = _workspaceWrapper.WorkspaceService.ResourceService.Registry.ResolveResourcePath(destResource);
            if (destPathResult.IsFailure)
            {
                return Result.Fail($"Failed to resolve path for resource: '{destResource}'")
                    .WithErrors(destPathResult);
            }
            var content = _fileTemplateService.GetNewFileContent(destPathResult.Value);
            var createResult = await opService.CreateFileAsync(destResource, content);
            if (createResult.IsFailure)
            {
                return Result.Fail($"Failed to create resource: {destResource}")
                    .WithErrors(createResult);
            }
            return Result.Ok();
        }

        // Copy from source path
        if (!File.Exists(sourcePath))
        {
            return Result.Fail($"Failed to create resource. Source file '{sourcePath}' does not exist.");
        }

        return await opService.ImportExternalFileAsync(sourcePath, destResource);
    }

    private static async Task<Result> CreateFolderAsync(
        string sourcePath,
        ResourceKey destResource,
        IResourceOperationService opService,
        IFileStorage fileStorage)
    {
        var infoResult = await fileStorage.GetInfoAsync(destResource);
        if (infoResult.IsSuccess
            && infoResult.Value.Kind != StorageItemKind.NotFound)
        {
            return Result.Fail($"A resource already exists at '{destResource}'.");
        }

        if (string.IsNullOrEmpty(sourcePath))
        {
            // Create a new empty folder
            var createResult = await opService.CreateFolderAsync(destResource);
            if (createResult.IsFailure)
            {
                return Result.Fail($"Failed to create folder: {destResource}")
                    .WithErrors(createResult);
            }
            return Result.Ok();
        }

        // Copy from source path
        if (!Directory.Exists(sourcePath))
        {
            return Result.Fail($"Failed to create resource. Source folder '{sourcePath}' does not exist.");
        }

        return await opService.ImportExternalFolderAsync(sourcePath, destResource);
    }

    private void ExpandParentFolder(ResourceKey destResource)
    {
        var parentFolderKey = destResource.GetParent();
        if (!parentFolderKey.IsEmpty)
        {
            _commandService.Execute<IExpandFolderCommand>(command =>
            {
                command.FolderResource = parentFolderKey;
                command.Expanded = true;
            });
        }
    }
}
