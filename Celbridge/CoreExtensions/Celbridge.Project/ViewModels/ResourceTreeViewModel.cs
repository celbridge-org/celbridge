using Celbridge.BaseLibrary.Clipboard;
using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Celbridge.Project.ViewModels;

public partial class ResourceTreeViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly IProjectService _projectService;
    private readonly IClipboardService _clipboardService;
    private readonly ICommandService _commandService;

    public ObservableCollection<IResource> Resources => _projectService.ResourceRegistry.RootFolder.Children;

    private bool _resourceTreeUpdatePending;

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
        _messengerService.Register<RequestResourceTreeUpdateMessage>(this, OnRequestResourceTreeUpdateMessage);
        _messengerService.Register<ExecutedCommandMessage>(this, OnExecutedCommandMessage);
    }

    public void OnUnloaded()
    {
        // Listen for messages to determine when to update the resource tree
        _messengerService.Unregister<RequestResourceTreeUpdateMessage>(this);
        _messengerService.Unregister<ExecutedCommandMessage>(this);
    }

    private void OnRequestResourceTreeUpdateMessage(object recipient, RequestResourceTreeUpdateMessage message)
    {
        // Set a flag to update the resource tree
        _resourceTreeUpdatePending = true;
    }

    private void OnExecutedCommandMessage(object recipient, ExecutedCommandMessage message)
    {
        if (_resourceTreeUpdatePending)
        {
            // Don't update if there are any pending resource tree commands
            if (!_commandService.ContainsCommandsOfType<IAddResourceCommand>() &&
                !_commandService.ContainsCommandsOfType<IDeleteResourceCommand>() &&
                !_commandService.ContainsCommandsOfType<ICopyResourceCommand>() &&
                !_commandService.ContainsCommandsOfType<IUpdateResourceTreeCommand>())
            {
                // Execute a command to update the resource tree
                _commandService.Execute<IUpdateResourceTreeCommand>();
                _resourceTreeUpdatePending = false;
            }
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

        bool currentState = resourceRegistry.IsFolderExpanded(folderResource);
        if (currentState == isExpanded)
        {
            return;
        }

        resourceRegistry.SetFolderIsExpanded(folderResource, isExpanded);

        // Save the workspace data (with a delay) to ensure the new expanded state is persisted
        _commandService.RemoveCommandsOfType<ISaveWorkspaceStateCommand>();
        _commandService.Execute<ISaveWorkspaceStateCommand>(250);
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

            if (sourceResource == destResource)
            {
                // Moving a resource to the same location is technically a no-op, but we still need to update
                // the resource tree because the TreeView may now be displaying the resources in the wrong order.
                _commandService.Execute<IUpdateResourceTreeCommand>();
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
        var rootFolder = _projectService.ResourceRegistry.RootFolder;

        IFolderResource destFolder;
        if (destResource is IFileResource file)
        {
            // Paste to the parent folder of the file resource
            destFolder = file.ParentFolder ?? rootFolder;
        }
        else if (destResource is IFolderResource folder)
        {
            // Paste to the folder resource
            destFolder = folder ?? rootFolder;
        }
        else
        {
            // Paste to the root folder
            destFolder = rootFolder;
        }

        var resourceRegistry = _projectService.ResourceRegistry;

        var destFolderResource = resourceRegistry.GetResourceKey(destFolder);

        // Execute a command to paste the clipboard content to the folder resource
        _commandService.Execute<IPasteResourceFromClipboardCommand>(command =>
        {
            command.DestFolderResource = destFolderResource;
        });
    }
}
