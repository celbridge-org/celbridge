using Celbridge.Commands;
using Celbridge.DataTransfer;

namespace Celbridge.Explorer.ViewModels.Helpers;

/// <summary>
/// Handles clipboard operations for the resource tree.
/// </summary>
public class ResourceTreeClipboardHelper
{
    private readonly ICommandService _commandService;
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IResourceTransferService _resourceTransferService;

    public ResourceTreeClipboardHelper(
        ICommandService commandService,
        IResourceRegistry resourceRegistry,
        IResourceTransferService resourceTransferService)
    {
        _commandService = commandService;
        _resourceRegistry = resourceRegistry;
        _resourceTransferService = resourceTransferService;
    }

    public void CutResourcesToClipboard(List<IResource> resources)
    {
        var resourceKeys = resources
            .Select(r => _resourceRegistry.GetResourceKey(r))
            .ToList();

        _commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResources = resourceKeys;
            command.TransferMode = DataTransferMode.Move;
        });
    }

    public void CopyResourcesToClipboard(List<IResource> resources)
    {
        var resourceKeys = resources
            .Select(r => _resourceRegistry.GetResourceKey(r))
            .ToList();

        _commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResources = resourceKeys;
            command.TransferMode = DataTransferMode.Copy;
        });
    }

    public void PasteResourceFromClipboard(IResource? destResource)
    {
        var destFolderResource = _resourceRegistry.GetContextMenuItemFolder(destResource);

        _commandService.Execute<IPasteResourceFromClipboardCommand>(command =>
        {
            command.DestFolderResource = destFolderResource;
        });
    }

    public Result ImportResources(List<string> sourcePaths, IResource? destResource)
    {
        if (destResource is null)
        {
            return Result.Fail("Destination resource is null");
        }

        var destFolderResource = _resourceRegistry.GetContextMenuItemFolder(destResource);

        var createResult = _resourceTransferService.CreateResourceTransfer(
            sourcePaths,
            destFolderResource,
            DataTransferMode.Copy);

        if (createResult.IsFailure)
        {
            return Result.Fail($"Failed to create resource transfer. {createResult.Error}");
        }

        var resourceTransfer = createResult.Value;

        var transferResult = _resourceTransferService.TransferResources(destFolderResource, resourceTransfer);
        if (transferResult.IsFailure)
        {
            return Result.Fail($"Failed to transfer resources. {transferResult.Error}");
        }

        return Result.Ok();
    }
}
