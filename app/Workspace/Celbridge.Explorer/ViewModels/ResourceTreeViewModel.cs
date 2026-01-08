using Celbridge.Commands;
using Celbridge.Console;
using Celbridge.DataTransfer;
using Celbridge.Documents;
using Celbridge.Explorer.Services;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Python;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Explorer.ViewModels;

public partial class ResourceTreeViewModel : ObservableObject
{
    private readonly ILogger<ResourceTreeViewModel> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IExplorerService _explorerService;
    private readonly IDocumentsService _documentsService;
    private readonly IDataTransferService _dataTransferService;
    private readonly IPythonService _pythonService;

    public IList<IResource> Resources => _explorerService.ResourceRegistry.RootFolder.Children;

    /// <summary>
    /// The name of the project folder displayed at the top of the tree view.
    /// </summary>
    public string ProjectFolderName { get; }

    public ResourceTreeViewModel(
        ILogger<ResourceTreeViewModel> logger,
        IMessengerService messengerService,
        ICommandService commandService,
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _commandService = commandService;
        _explorerService = workspaceWrapper.WorkspaceService.ExplorerService;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;
        _dataTransferService = workspaceWrapper.WorkspaceService.DataTransferService;
        _pythonService = workspaceWrapper.WorkspaceService.PythonService;

        // Get the project folder name from the current project
        ProjectFolderName = projectService.CurrentProject?.ProjectName ?? string.Empty;
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

        _messengerService.Register<ClipboardContentChangedMessage>(this, OnClipboardContentChangedMessage);
    }

    public void OnUnloaded()
    {
        _messengerService.UnregisterAll(this);
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
    /// Set to true if the selected context menu item is a valid file or folder resource.
    /// </summary>
    [ObservableProperty]
    private bool _isResourceSelected;

    /// <summary>
    /// Set to true if the selected context menu item is a file resource that can be opened as a document.
    /// </summary>
    [ObservableProperty]
    private bool _isDocumentResourceSelected;

    /// <summary>
    /// Set to true if the selected context menu item is an executable script that can be run.
    /// </summary>
    [ObservableProperty]
    private bool _isExecutableResourceSelected;

    /// <summary>
    /// Set to true if the clipboard content contains a resource.
    /// </summary>
    [ObservableProperty]
    private bool _isResourceOnClipboard;

    /// <summary>
    /// Set to true if the selected context menu item is a file resource (not a folder) that can be opened with an application.
    /// </summary>
    [ObservableProperty]
    private bool _isFileResourceSelected;

    private async Task UpdateContextMenuOptions(IResource? resource)
    {
        // Some context menu options are only available if a resource is selected
        IsResourceSelected = resource is not null;

        // The Open context menu option is only available for file resources that can be opened as documents
        IsDocumentResourceSelected = IsSupportedDocumentFormat(resource);

        // The Run context menu option is only available for script resources that can be executed
        IsExecutableResourceSelected = IsResourceExecutable(resource);

        // The "Open with Application" context menu option is only available for file resources (not folders)
        IsFileResourceSelected = resource is IFileResource;

        // The Paste context menu option is only available if there is a resource on the clipboard
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

    private bool IsSupportedDocumentFormat(IResource? resource)
    {
        if (resource is not null &&
            resource is IFileResource fileResource)
        {
            var resourceRegistry = _explorerService.ResourceRegistry;
            var resourceKey = resourceRegistry.GetResourceKey(fileResource);
            var documentType = _documentsService.GetDocumentViewType(resourceKey);

            return documentType != DocumentViewType.UnsupportedFormat;
        }

        return false;
    }

    private bool IsResourceExecutable(IResource? resource)
    {
        if (resource is not null &&
            resource is IFileResource fileResource)
        {
            var resourceRegistry = _explorerService.ResourceRegistry;
            var resourceKey = resourceRegistry.GetResourceKey(fileResource);

            var extension = Path.GetExtension(resourceKey);

            if (extension == ExplorerConstants.PythonExtension ||
                extension == ExplorerConstants.IPythonExtension)
            {
                // Only enable the Run option if the python host is available to run the script
                return _pythonService.IsPythonHostAvailable;
            }
        }

        return false;
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

    public void RefreshExplorer()
    {
        _commandService.Execute<IUpdateResourcesCommand>();
    }

    public void OpenProjectSettings()
    {
        // Get the project file path and open it as a document
        var projectService = ServiceLocator.AcquireService<IProjectService>();
        var currentProject = projectService.CurrentProject;
        if (currentProject is null)
        {
            return;
        }

        // Get the project file name (e.g., "myproject.celbridge")
        var projectFilePath = currentProject.ProjectFilePath;
        var projectFileName = Path.GetFileName(projectFilePath);

        // Create a resource key for the project file
        var fileResource = new ResourceKey(projectFileName);

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
        });
    }

    //
    // Resource editing
    //

    public void RunScript(IFileResource scriptResource)
    {
        var resourceRegistry = _explorerService.ResourceRegistry;
        var resource = resourceRegistry.GetResourceKey(scriptResource);

        var extension = Path.GetExtension(resource);

        if (extension != ExplorerConstants.PythonExtension &&
            extension != ExplorerConstants.IPythonExtension)
        {
            // Attempting to run a non Python resource has no effect
            return;
        }

        // Todo: Read an argument string from the meta data for the resource

        _commandService.Execute<IRunCommand>(command =>
        {
            command.ScriptResource = resource;
        });
    }

    public void OpenDocument(IFileResource fileResource)
    {
        if (!IsSupportedDocumentFormat(fileResource))
        {
            // Attempting to open an unsupported resource has no effect.
            return;
        }

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
            command.ResourceType = resourceType; // File or folder
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

    public void OpenResourceInExplorer(IResource? resource)
    {
        // A null resource here indicates the root folder
        var resourceRegistry = _explorerService.ResourceRegistry;
        var resourceKey = resource is null ? ResourceKey.Empty : resourceRegistry.GetResourceKey(resource);

        // Execute a command to open the resource in the system file manager
        _commandService.Execute<IOpenFileManagerCommand>(command =>
        {
            command.Resource = resourceKey;
        });
    }

    public void OpenResourceInApplication(IResource? resource)
    {
        // A null resource here indicates the root folder
        var resourceRegistry = _explorerService.ResourceRegistry;
        var resourceKey = resource is null ? ResourceKey.Empty : resourceRegistry.GetResourceKey(resource);

        if(!resourceKey.IsEmpty)
        {
            _explorerService.OpenResource(resourceKey);
        }
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

    public void CopyResourceKeyToClipboard(IResource resource)
    {
        var resourceRegistry = _explorerService.ResourceRegistry;
        var resourceKey = resourceRegistry.GetResourceKey(resource);

        _commandService.Execute<ICopyTextToClipboardCommand>(command =>
        {
            command.Text = resourceKey;
            command.TransferMode = DataTransferMode.Copy;
        });
    }

    public void CopyFilePathToClipboard(IResource resource)
    {
        var resourceRegistry = _explorerService.ResourceRegistry;
        var filePath = resourceRegistry.GetResourcePath(resource);

        _commandService.Execute<ICopyTextToClipboardCommand>(command =>
        {
            command.Text = filePath;
            command.TransferMode = DataTransferMode.Copy;
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

    public void OnSelectedResourceChanged(ResourceKey resource)
    {
        // Notify listeners that the selected resource has changed
        var message = new SelectedResourceChangedMessage(resource);
        _messengerService.Send(message);
    }
}
