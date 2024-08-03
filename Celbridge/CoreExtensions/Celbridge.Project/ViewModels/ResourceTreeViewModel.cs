using Celbridge.Clipboard;
using Celbridge.Commands;
using Celbridge.Resources;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Projects.ViewModels;

public partial class ResourceTreeViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly IProjectService _projectService;
    private readonly IClipboardService _clipboardService;
    private readonly ICommandService _commandService;

    public IList<IResource> Resources => _projectService.ResourceRegistry.RootFolder.Children;

    public ResourceTreeViewModel(
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService)
    {
        _messengerService = messengerService;
        _projectService = workspaceWrapper.WorkspaceService.ProjectService;
        _clipboardService = workspaceWrapper.WorkspaceService.ClipboardService;
        _commandService = commandService;
    }

    //
    // Event handlers
    //

    public void OnLoaded()
    {
        // Listen for messages to determine when to update the resource tree
        _messengerService.Register<RequestResourceRegistryUpdateMessage>(this, OnRequestResourceTreeUpdateMessage);
    }

    public void OnUnloaded()
    {
        // Listen for messages to determine when to update the resource tree
        _messengerService.Unregister<RequestResourceRegistryUpdateMessage>(this);
    }

    private void OnRequestResourceTreeUpdateMessage(object recipient, RequestResourceRegistryUpdateMessage message)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        resourceRegistry.UpdateResourceRegistry();
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

        bool isResourceOnClipbaord = false;
        if (_clipboardService.GetClipboardContentType() == ClipboardContentType.Resource)
        {
            var resourceRegistry = _projectService.ResourceRegistry;
            var destFolderResource = resourceRegistry.GetContextMenuItemFolder(resource);

            var getResult = await _clipboardService.GetClipboardResourceContent(destFolderResource);
            if (getResult.IsSuccess)
            {
                var content = getResult.Value;
                isResourceOnClipbaord = content.ResourceItems.Count > 0;
            }
        }
        IsResourceOnClipboard = isResourceOnClipbaord;
    }

    //
    // Tree View state
    //

    public void SetFolderIsExpanded(IFolderResource folder, bool isExpanded)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
        var folderResource = resourceRegistry.GetResourceKey(folder);

        bool currentRegistryState = resourceRegistry.IsFolderExpanded(folderResource);
        bool curentFolderState = folder.IsExpanded;

        if (currentRegistryState == isExpanded &&
            curentFolderState == isExpanded)
        {
            return;
        }

        resourceRegistry.SetFolderIsExpanded(folderResource, isExpanded);
        folder.IsExpanded = isExpanded;
    }

    //
    // Resource editing
    //

    public void ShowAddResourceDialog(ResourceType resourceType, IFolderResource? destFolder)
    {
        var resourceRegistry = _projectService.ResourceRegistry;

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
        var resourceRegistry = _projectService.ResourceRegistry;
        var resourceKey = resourceRegistry.GetResourceKey(resource);

        // Execute a command to show the delete resource dialog
        _commandService.Execute<IDeleteResourceDialogCommand>(command =>
        {
            command.Resource = resourceKey;
        });
    }

    public void ShowRenameResourceDialog(IResource resource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;
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
            destFolder = _projectService.ResourceRegistry.RootFolder;
        }

        foreach (var resource in resources)
        {
            var sourceResource = _projectService.ResourceRegistry.GetResourceKey(resource);
            var destResource = _projectService.ResourceRegistry.GetResourceKey(destFolder);
            var resolvedDestResource = _projectService.ResourceRegistry.GetCopyDestinationResource(sourceResource, destResource);

            if (sourceResource == resolvedDestResource)
            {
                // Moving a resource to the same location is technically a no-op, but we still need to update
                // the resource tree because the TreeView may now be displaying the resources in the wrong order.
                _commandService.Execute<IUpdateResourceRegistryCommand>();
                continue;
            }

            _commandService.Execute<ICopyResourceCommand>(command =>
            {
                command.SourceResource = sourceResource;
                command.DestResource = destResource;
                command.Operation = CopyResourceOperation.Move;
            });
        }
    }

    //
    // Clipboard support
    //

    public void CutResourceToClipboard(IResource sourceResource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;

        var sourceResourceKey = resourceRegistry.GetResourceKey(sourceResource);

        // Execute a command to cut the resource to the clipboard
        _commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResource = sourceResourceKey;
            command.Operation = CopyResourceOperation.Move;
        });
    }

    public void CopyResourceToClipboard(IResource sourceResource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;

        var resourceKey = resourceRegistry.GetResourceKey(sourceResource);

        // Execute a command to copy the resource to the clipboard
        _commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResource = resourceKey;
            command.Operation = CopyResourceOperation.Copy;
        });
    }

    public void PasteResourceFromClipboard(IResource? destResource)
    {
        var resourceRegistry = _projectService.ResourceRegistry;

        var destFolderResource = resourceRegistry.GetContextMenuItemFolder(destResource);

        // Execute a command to paste the clipboard content to the folder resource
        _commandService.Execute<IPasteResourceFromClipboardCommand>(command =>
        {
            command.DestFolderResource = destFolderResource;
        });
    }
}
