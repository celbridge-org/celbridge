using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Documents;
using Celbridge.Explorer.Services;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Explorer.ViewModels;

public partial class ResourceTreeViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly IExplorerService _explorerService;
    private readonly IDataTransferService _dataTransferService;
    private readonly ICommandService _commandService;

    public IList<IResource> Resources => _explorerService.ResourceRegistry.RootFolder.Children;

    private bool _isWorkspaceLoaded;

    public ResourceTreeViewModel(
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService)
    {
        _messengerService = messengerService;
        _explorerService = workspaceWrapper.WorkspaceService.ExplorerService;
        _dataTransferService = workspaceWrapper.WorkspaceService.DataTransferService;
        _commandService = commandService;
    }

    //
    // Event handlers
    //

    public void OnLoaded(IResourceTreeView resourceTreeView)
    {
        // Use the concrete type to set the resource tree view because the
        // interface does not expose the setter.

        var explorerService = _explorerService as ExplorerService;
        Guard.IsNotNull(explorerService);

        explorerService.ResourceTreeView = resourceTreeView;

        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoadedMessage);
        _messengerService.Register<ClipboardContentChangedMessage>(this, OnClipboardContentChangedMessage);        
    }

    public void OnUnloaded()
    {
        _messengerService.Unregister<WorkspaceLoadedMessage>(this);
        _messengerService.Unregister<ClipboardContentChangedMessage>(this);
    }

    private void OnWorkspaceLoadedMessage(object recipient, WorkspaceLoadedMessage message)
    {
        // This will remain true for the lifetime of this view model
        _isWorkspaceLoaded = true;
    }

    private void OnClipboardContentChangedMessage(object recipient, ClipboardContentChangedMessage message)
    {
        var contentDescription = _dataTransferService.GetClipboardContentDescription();

        if (contentDescription.ContentType == ClipboardContentType.Resource)
        {
            // Todo: Clear previously faded resources
        }

        if (contentDescription.ContentOperation == ClipboardContentOperation.Move)
        {
            // Todo: Fade cut resources in the tree view
        }
    }

    public void OnContextMenuOpening(IResource? resource)
    {
        _ = UpdateContextMenuOptions(resource);
    }

    /// <summary>
    /// Set to true if the current context menu item is a valid resource.
    /// </summary>
    [ObservableProperty]
    private bool _isResourceSelected;

    /// <summary>
    /// Set to true if the clipboard content contains a resource.
    /// </summary>
    [ObservableProperty]
    private bool _isResourceOnClipboard;

    private async Task UpdateContextMenuOptions(IResource? resource)
    {
        IsResourceSelected = resource is not null;

        bool isResourceOnClipboard = false;

        var contentDescription = _dataTransferService.GetClipboardContentDescription();

        if (contentDescription.ContentType == ClipboardContentType.Resource)
        {
            var resourceRegistry = _explorerService.ResourceRegistry;
            var destFolderResource = resourceRegistry.GetContextMenuItemFolder(resource);

            var getResult = await _dataTransferService.GetClipboardResourceTransfer(destFolderResource);
            if (getResult.IsSuccess)
            {
                var content = getResult.Value;
                isResourceOnClipboard = content.TransferItems.Count > 0;
            }
        }
        IsResourceOnClipboard = isResourceOnClipboard;
    }

    //
    // Tree View state
    //

    public void SetFolderIsExpanded(IFolderResource folder, bool isExpanded)
    {
        var resourceRegistry = _explorerService.ResourceRegistry;
        var folderResource = resourceRegistry.GetResourceKey(folder);

        bool currentRegistryState = resourceRegistry.IsFolderExpanded(folderResource);
        bool curentFolderState = folder.IsExpanded;

        if (currentRegistryState == isExpanded &&
            curentFolderState == isExpanded)
        {
            return;
        }

        _commandService.Execute<IExpandFolderCommand>(command =>
        {
            command.FolderResource = folderResource;
            command.Expanded = isExpanded;
            command.UpdateResources = false; // TreeView has already expanded the folder node, no need to update it again.
        });
    }

    //
    // Resource editing
    //

    public void OpenFileResource(IFileResource fileResource)
    {
        var resourceRegistry = _explorerService.ResourceRegistry;
        var resource = resourceRegistry.GetResourceKey(fileResource);

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = resource;
        });
    }

    public void ShowAddResourceDialog(ResourceType resourceType, IFolderResource? destFolder)
    {
        var resourceRegistry = _explorerService.ResourceRegistry;

        if (destFolder is null)
        {
            // If the destination folder is null, add the new folder to the root folder
            destFolder = resourceRegistry.RootFolder;
        }

        var destFolderResource = resourceRegistry.GetResourceKey(destFolder);

        // Execute a command to show the add resource dialog
        _commandService.Execute<IAddResourceDialogCommand>(command =>
        {
            command.ResourceType = resourceType;
            command.DestFolderResource = destFolderResource;
        });
    }

    public void ShowDeleteResourceDialog(IResource resource)
    {
        var resourceRegistry = _explorerService.ResourceRegistry;
        var resourceKey = resourceRegistry.GetResourceKey(resource);

        // Execute a command to show the delete resource dialog
        _commandService.Execute<IDeleteResourceDialogCommand>(command =>
        {
            command.Resource = resourceKey;
        });
    }

    public void ShowRenameResourceDialog(IResource resource)
    {
        var resourceRegistry = _explorerService.ResourceRegistry;
        var resourceKey = resourceRegistry.GetResourceKey(resource);

        // Execute a command to show the rename resource dialog
        _commandService.Execute<IRenameResourceDialogCommand>(command =>
        {
            command.Resource = resourceKey;
        });
    }

    public void MoveResourcesToFolder(List<IResource> resources, IFolderResource? destFolder)
    {
        if (destFolder is null)
        {
            // A null folder reference indicates the root folder
            destFolder = _explorerService.ResourceRegistry.RootFolder;
        }

        foreach (var resource in resources)
        {
            var sourceResource = _explorerService.ResourceRegistry.GetResourceKey(resource);
            var destResource = _explorerService.ResourceRegistry.GetResourceKey(destFolder);
            var resolvedDestResource = _explorerService.ResourceRegistry.ResolveDestinationResource(sourceResource, destResource);

            if (sourceResource == resolvedDestResource)
            {
                // Moving a resource to the same location is technically a no-op, but we still need to update
                // the resource tree because the TreeView may now be displaying the resources in the wrong order.
                _commandService.Execute<IUpdateResourcesCommand>();
                continue;
            }

            _commandService.Execute<ICopyResourceCommand>(command =>
            {
                command.SourceResource = sourceResource;
                command.DestResource = destResource;
                command.TransferMode = DataTransferMode.Move;
            });
        }
    }

    //
    // Clipboard support
    //

    public void CutResourceToClipboard(IResource sourceResource)
    {
        var resourceRegistry = _explorerService.ResourceRegistry;

        var sourceResourceKey = resourceRegistry.GetResourceKey(sourceResource);

        // Execute a command to cut the resource to the clipboard
        _commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResource = sourceResourceKey;
            command.TransferMode = DataTransferMode.Move;
        });
    }

    public void CopyResourceToClipboard(IResource sourceResource)
    {
        var resourceRegistry = _explorerService.ResourceRegistry;

        var resourceKey = resourceRegistry.GetResourceKey(sourceResource);

        // Execute a command to copy the resource to the clipboard
        _commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResource = resourceKey;
            command.TransferMode = DataTransferMode.Copy;
        });
    }

    public void PasteResourceFromClipboard(IResource? destResource)
    {
        var resourceRegistry = _explorerService.ResourceRegistry;

        var destFolderResource = resourceRegistry.GetContextMenuItemFolder(destResource);

        // Execute a command to paste the clipboard content to the folder resource
        _commandService.Execute<IPasteResourceFromClipboardCommand>(command =>
        {
            command.DestFolderResource = destFolderResource;
        });
    }

    public async Task<Result> ImportResources(List<string> sourcePaths, IResource? destResource)
    {
        if (destResource is null)
        {
            return Result.Fail("Destination resource is null");
        }

        var destFolderResource = _explorerService.ResourceRegistry.GetContextMenuItemFolder(destResource);
        var createResult = _explorerService.CreateResourceTransfer(sourcePaths, destFolderResource, DataTransferMode.Copy);
        if (createResult.IsFailure)
        {
            return Result.Fail($"Failed to create resource transfer. {createResult.Error}");
        }
        var resourceTransfer = createResult.Value;

        var transferResult = await _explorerService.TransferResources(destFolderResource, resourceTransfer);
        if (transferResult.IsFailure)
        {
            return Result.Fail($"Failed to transfer resources. {transferResult.Error}");
        }

        return Result.Ok();
    }

    public void OnSelectedResourceChanged(ResourceKey selectedResource)
    {
        if (_isWorkspaceLoaded)
        {
            // Ignore change events that happen while loading the workspace
            _ = _explorerService.StoreSelectedResource(selectedResource);
        }
    }
}
