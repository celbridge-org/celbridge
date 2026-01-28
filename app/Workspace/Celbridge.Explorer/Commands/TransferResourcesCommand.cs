using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Dialog;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Commands;

public class TransferResourcesCommand : CommandBase, ITransferResourcesCommand
{
    public override CommandFlags CommandFlags => CommandFlags.RequestUpdateResources;

    public ResourceKey DestFolderResource { get; set; }
    public DataTransferMode TransferMode { get; set; }
    public List<ResourceTransferItem> TransferItems { get; set; } = new();

    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public TransferResourcesCommand(
        IDialogService dialogService,
        IStringLocalizer stringLocalizer,
        IWorkspaceWrapper workspaceWrapper)
    {
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Workspace is not loaded");
        }

        if (TransferItems.Count == 0)
        {
            return Result.Ok();
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ResourceService.Registry;
        var fileOpService = workspaceService.FileOperationService;

        // Filter out any items where the destination resource already exists
        TransferItems.RemoveAll(item => resourceRegistry.GetResource(item.DestResource).IsSuccess);

        if (TransferItems.Count == 0)
        {
            return Result.Ok();
        }

        // Begin batch for single undo operation
        fileOpService.BeginBatch();

        List<string> failedItems = new();

        try
        {
            foreach (var item in TransferItems)
            {
                var result = await TransferSingleItemAsync(item, resourceRegistry, fileOpService);
                if (result.IsFailure)
                {
                    failedItems.Add(item.DestResource.ResourceName);
                }
            }
        }
        finally
        {
            // Always commit batch - partial success is acceptable
            fileOpService.CommitBatch();
        }

        // Expand the destination folder so the user can see the newly transferred resources
        resourceRegistry.SetFolderIsExpanded(DestFolderResource, true);

        // Show error dialog if any items failed
        if (failedItems.Count > 0)
        {
            await ShowTransferErrorAsync(failedItems);
        }

        return Result.Ok();
    }

    private async Task<Result> TransferSingleItemAsync(
        ResourceTransferItem item,
        IResourceRegistry resourceRegistry,
        IFileOperationService fileOpService)
    {
        if (item.SourceResource.IsEmpty)
        {
            // Resource is outside the project folder - add it
            return await AddExternalResourceAsync(item, resourceRegistry, fileOpService);
        }
        else
        {
            // Resource is inside the project folder - copy/move it
            return await CopyInternalResourceAsync(item, resourceRegistry, fileOpService);
        }
    }

    private async Task<Result> AddExternalResourceAsync(
        ResourceTransferItem item,
        IResourceRegistry resourceRegistry,
        IFileOperationService fileOpService)
    {
        var destPath = resourceRegistry.GetResourcePath(item.DestResource);

        if (item.ResourceType == ResourceType.File)
        {
            if (!File.Exists(item.SourcePath))
            {
                return Result.Fail($"Source file does not exist: {item.SourcePath}");
            }

            return await fileOpService.CopyFileAsync(item.SourcePath, destPath);
        }
        else if (item.ResourceType == ResourceType.Folder)
        {
            if (!Directory.Exists(item.SourcePath))
            {
                return Result.Fail($"Source folder does not exist: {item.SourcePath}");
            }

            return await fileOpService.CopyFolderAsync(item.SourcePath, destPath);
        }

        return Result.Fail($"Invalid resource type: {item.ResourceType}");
    }

    private async Task<Result> CopyInternalResourceAsync(
        ResourceTransferItem item,
        IResourceRegistry resourceRegistry,
        IFileOperationService fileOpService)
    {
        var resolvedDestResource = resourceRegistry.ResolveDestinationResource(item.SourceResource, item.DestResource);
        
        var sourcePath = resourceRegistry.GetResourcePath(item.SourceResource);
        var destPath = resourceRegistry.GetResourcePath(resolvedDestResource);

        var result = await fileOpService.TransferAsync(sourcePath, destPath, TransferMode);

        // Expand destination parent folder
        if (result.IsSuccess)
        {
            var parentFolder = resolvedDestResource.GetParent();
            if (!parentFolder.IsEmpty)
            {
                resourceRegistry.SetFolderIsExpanded(parentFolder, true);
            }
        }

        return result;
    }

    private async Task ShowTransferErrorAsync(List<string> failedItems)
    {
        var titleKey = TransferMode == DataTransferMode.Copy 
            ? "ResourceTree_CopyResource" 
            : "ResourceTree_MoveResource";
        
        var title = _stringLocalizer.GetString(titleKey);
        var failedList = string.Join(", ", failedItems);
        var message = _stringLocalizer.GetString("ResourceTree_TransferResourcesFailed", failedList);
        
        await _dialogService.ShowAlertDialogAsync(title, message);
    }
}
