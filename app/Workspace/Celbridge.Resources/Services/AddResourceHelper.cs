using Celbridge.Commands;
using Celbridge.Explorer;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

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

        var destPath = resourceRegistry.GetResourcePath(destResource);

        // Fail if the parent folder for the new resource does not exist.
        var parentFolderPath = Path.GetDirectoryName(destPath);
        if (!Directory.Exists(parentFolderPath))
        {
            return Result.Fail($"Failed to create resource. Parent folder does not exist: '{parentFolderPath}'");
        }

        var createResult = resourceType == ResourceType.File
            ? await CreateFileAsync(sourcePath, destPath, destResource, resourceOpService)
            : await CreateFolderAsync(sourcePath, destPath, destResource, resourceOpService);

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
        string destPath,
        ResourceKey destResource,
        IResourceOperationService opService)
    {
        if (File.Exists(destPath))
        {
            return Result.Fail($"A file already exists at '{destPath}'.");
        }

        if (string.IsNullOrEmpty(sourcePath))
        {
            // Create a new empty file
            var content = _fileTemplateService.GetNewFileContent(destPath);
            var createResult = await opService.CreateFileAsync(destPath, content);
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

        return await opService.CopyFileAsync(sourcePath, destPath);
    }

    private static async Task<Result> CreateFolderAsync(
        string sourcePath,
        string destPath,
        ResourceKey destResource,
        IResourceOperationService opService)
    {
        if (Directory.Exists(destPath))
        {
            return Result.Fail($"A folder already exists at '{destPath}'.");
        }

        if (string.IsNullOrEmpty(sourcePath))
        {
            // Create a new empty folder
            var createResult = await opService.CreateFolderAsync(destPath);
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

        return await opService.CopyFolderAsync(sourcePath, destPath);
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
