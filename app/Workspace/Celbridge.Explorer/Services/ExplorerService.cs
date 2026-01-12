using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Projects;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.Utilities.Services;
using Celbridge.Utilities;
using Celbridge.Workspace;
using Celbridge.Logging;

namespace Celbridge.Explorer.Services;

public class ExplorerService : IExplorerService, IDisposable
{
    private const string PreviousSelectedResourceKey = "PreviousSelectedResource";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExplorerService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IEditorSettings _editorSettings;
    private readonly ICommandService _commandService;
    private readonly IProjectService _projectService;
    private readonly IIconService _iconService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ResourceChangeMonitor? _resourceChangeMonitor;

    private IExplorerPanel? _explorerPanel;
    public IExplorerPanel ExplorerPanel => _explorerPanel!;

    private ISearchPanel? _searchPanel;
    public ISearchPanel SearchPanel => _searchPanel!;

    public IResourceRegistry ResourceRegistry { get; init; }

    private IResourceTreeView? _resourceTreeView;
    public IResourceTreeView ResourceTreeView 
    {
        get
        {
            return _resourceTreeView ?? throw new NullReferenceException("ResourceTreeView is null.");
        }
        set 
        { 
            _resourceTreeView = value; 
        }
    }

    public ResourceKey SelectedResource { get; private set; }

    private bool _isWorkspaceLoaded;

    public ExplorerService(
        IServiceProvider serviceProvider,
        ILogger<ExplorerService> logger,
        IMessengerService messengerService,
        IEditorSettings editorSettings,
        ICommandService commandService,
        IProjectService projectService,
        IUtilityService utilityService,
        IIconService iconService,
        IWorkspaceWrapper workspaceWrapper,
        ResourceChangeMonitor resourceChangeMonitor)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        _serviceProvider = serviceProvider;
        _logger = logger;
        _messengerService = messengerService;
        _editorSettings = editorSettings;
        _commandService = commandService;
        _projectService = projectService;
        _iconService = iconService;
        _workspaceWrapper = workspaceWrapper;
        _resourceChangeMonitor = resourceChangeMonitor;

        // Delete the DeletedFiles folder to clean these archives up.
        // The DeletedFiles folder contain archived files and folders from previous delete commands.
        var tempFilename = utilityService.GetTemporaryFilePath(PathConstants.DeletedFilesFolder, string.Empty);
        var deletedFilesFolder = Path.GetDirectoryName(tempFilename)!;
        if (Directory.Exists(deletedFilesFolder))
        {
            Directory.Delete(deletedFilesFolder, true);
        }

        // Create the resource registry for the project.
        // The registry is populated later once the workspace UI is fully loaded.
        ResourceRegistry = _serviceProvider.GetRequiredService<IResourceRegistry>();
        ResourceRegistry.ProjectFolderPath = _projectService.CurrentProject!.ProjectFolderPath;

        // Initialize the resource change monitor
        InitializeResourceChangeMonitor();

        _messengerService.Register<WorkspaceWillPopulatePanelsMessage>(this, OnWorkspaceWillPopulatePanelsMessage);
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoadedMessage);
        _messengerService.Register<SelectedResourceChangedMessage>(this, OnSelectedResourceChangedMessage);
    }

    private void InitializeResourceChangeMonitor()
    {
        try
        {
            Guard.IsNotNull(_resourceChangeMonitor);
            
            var initResult = _resourceChangeMonitor.Initialize();
            if (initResult.IsFailure)
            {
                _logger.LogWarning(initResult, "Failed to initialize resource change monitor");
                _resourceChangeMonitor?.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception occurred while initializing resource change monitor: {ex.Message}");
            _resourceChangeMonitor?.Dispose();
        }
    }

    private void OnWorkspaceWillPopulatePanelsMessage(object recipient, WorkspaceWillPopulatePanelsMessage message)
    {
        _explorerPanel = _serviceProvider.GetRequiredService<IExplorerPanel>();
        _searchPanel = _serviceProvider.GetRequiredService<ISearchPanel>();
    }

    private void OnWorkspaceLoadedMessage(object recipient, WorkspaceLoadedMessage message)
    {
        // Once set, this will remain true for the lifetime of the service
        _isWorkspaceLoaded = true;
    }

    private void OnSelectedResourceChangedMessage(object recipient, SelectedResourceChangedMessage message)
    {
        SelectedResource = message.Resource;

        if (_isWorkspaceLoaded)
        {
            // Ignore change events that happen while loading the workspace
            _ = StoreSelectedResource();            
        }
    }

    public async Task<Result> UpdateResourcesAsync()
    {
        var updateResult = ResourceRegistry.UpdateResourceRegistry();
        if (updateResult.IsFailure)
        {
            return Result.Fail($"Failed to update resources. {updateResult.Error}");
        }

        var populateResult = await ResourceTreeView.PopulateTreeView(ResourceRegistry);
        if (populateResult.IsFailure)
        {
            return Result.Fail($"Failed to update resources. {populateResult.Error}");
        }

        return Result.Ok();
    }

    public Result<IResourceTransfer> CreateResourceTransfer(List<string> sourcePaths, ResourceKey destFolderResource, DataTransferMode transferMode)
    {
        var createItemsResult = CreateResourceTransferItems(sourcePaths, destFolderResource);
        if (createItemsResult.IsFailure)
        {
            return Result<IResourceTransfer>.Fail($"Failed to create resource transfer items.")
                .WithErrors(createItemsResult);
        }
        var transferItems = createItemsResult.Value;

        var resourceTransfer = new ResourceTransfer()
        {
            TransferMode = transferMode,
            TransferItems = transferItems
        };

        return Result<IResourceTransfer>.Ok(resourceTransfer);
    }

    private Result<List<ResourceTransferItem>> CreateResourceTransferItems(List<string> sourcePaths, ResourceKey destFolderResource)
    {
        try
        {
            List<ResourceTransferItem> transferItems = new();

            var destFolderPath = ResourceRegistry.GetResourcePath(destFolderResource);
            if (!Directory.Exists(destFolderPath))
            {
                return Result<List<ResourceTransferItem>>.Fail($"The path '{destFolderPath}' does not exist.");
            }

            foreach (var sourcePath in sourcePaths)
            {
                if (PathContainsSubPath(destFolderPath, sourcePath) &&
                    string.Compare(destFolderPath, sourcePath, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    // Ignore attempts to transfer a resource into a subfolder of itself.
                    // This check is case insensitive to err on the safe side for Windows file systems.
                    // Without this check, a transfer operation could generate thousands of nested folders!
                    // It is ok to "transfer" a resource to the same path however as this indicates a duplicate operation.
                    return Result<List<ResourceTransferItem>>.Fail($"Cannot transfer a resource into a subfolder of itself.");
                }

                ResourceType resourceType = ResourceType.Invalid;
                if (File.Exists(sourcePath))
                {
                    resourceType = ResourceType.File;
                }
                else if (Directory.Exists(sourcePath))
                {
                    resourceType = ResourceType.Folder;
                }
                else
                {
                    // Resource does not exist in the file system, ignore it.
                    continue;
                }

                var getKeyResult = ResourceRegistry.GetResourceKey(sourcePath);
                if (getKeyResult.IsSuccess)
                {
                    // This resource is inside the project folder so we should use the CopyResource command
                    // to copy/move it so that the resource meta data is preserved.
                    // This is indicated by having a non-empty source resource property.

                    var sourceResource = getKeyResult.Value;

                    // Sanity check that the generated sourceResource matches the original source path
                    var checkSourcePath = ResourceRegistry.GetResourcePath(sourceResource);
                    Guard.IsTrue(sourcePath == checkSourcePath);

                    var destResource = ResourceRegistry.ResolveDestinationResource(sourceResource, destFolderResource);

                    var item = new ResourceTransferItem(resourceType, sourcePath, sourceResource, destResource);
                    transferItems.Add(item);
                }
                else
                {
                    // This file or folder resource is outside the project folder, so we should add it to the project
                    // via the AddResource command, which will create new metadata for the resource.
                    // This behaviour is indicated by having an empty source resource property.
                    var sourceResource = new ResourceKey();
                    var resourcename = Path.GetFileName(sourcePath);
                    var destResource = destFolderResource.Combine(resourcename);

                    var item = new ResourceTransferItem(resourceType, sourcePath, sourceResource, destResource);
                    transferItems.Add(item);
                }
            }

            if (transferItems.Count == 0)
            {
                return Result<List<ResourceTransferItem>>.Fail($"Transfer item list is empty.");
            }

            return Result<List<ResourceTransferItem>>.Ok(transferItems);
        }
        catch (Exception ex)
        {
            return Result<List<ResourceTransferItem>>.Fail($"Failed to create resource transfer items.")
                .WithException(ex);
        }
    }

    public async Task<Result> TransferResources(ResourceKey destFolderResource, IResourceTransfer transfer)
    {
        // Filter out any items where the destination resource already exists
        // Todo: If it's a single item, ask the user if they want to replace the existing resource
        transfer.TransferItems.RemoveAll(item =>
        {
            return ResourceRegistry.GetResource(item.DestResource).IsSuccess;
        });

        if (transfer.TransferItems.Count == 0)
        {
            // All resource items have been filtered out so nothing left to transfer
            return Result.Ok();
        }

        // If there are multiple items, assign the same undo group id to all commands.
        // This ensures that all commands are undone together in a single operation.
        var undoGroupId = transfer.TransferItems.Count > 1 ? EntityId.Create() : EntityId.InvalidId;

        foreach (var transferItem in transfer.TransferItems)
        {
            if (transferItem.SourceResource.IsEmpty)
            {
                // This resource is outside the project folder, add it using the AddResource command.
                _commandService.Execute<IAddResourceCommand>(command =>
                {
                    command.ResourceType = transferItem.ResourceType;
                    command.DestResource = transferItem.DestResource;
                    command.SourcePath = transferItem.SourcePath;
                    command.UndoGroupId = undoGroupId;
                });
            }
            else
            {
                // This resource is inside the project folder, copy/move it using the CopyResource command.
                _commandService.Execute<ICopyResourceCommand>(command =>
                {
                    command.SourceResource = transferItem.SourceResource;
                    command.DestResource = transferItem.DestResource;
                    command.TransferMode = transfer.TransferMode;
                    command.UndoGroupId = undoGroupId;
                });
            }
        }

        // Expand the destination folder so the user can see the newly transfered resources immediately.
        ResourceRegistry.SetFolderIsExpanded(destFolderResource, true);

        await Task.CompletedTask;

        return Result.Ok();
    }

    public async Task<Result> SelectResource(ResourceKey resource, bool showExplorerPanel)
    {
        Guard.IsNotNull(ExplorerPanel);

        var selectResult = await ExplorerPanel.SelectResource(resource);
        if (selectResult.IsFailure)
        {
            return Result.Fail($"Failed to select resource: {resource}")
                .WithErrors(selectResult);
        }

        if (showExplorerPanel)
        {
            _commandService.Execute<ISetPanelVisibilityCommand>(command =>
            {
                command.Panels = PanelVisibilityFlags.Context;
                command.IsVisible = true;
            });
        }

        return Result.Ok();
    }

    public async Task StoreSelectedResource()
    {
        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        Guard.IsNotNull(workspaceSettings);

        await workspaceSettings.SetPropertyAsync(PreviousSelectedResourceKey, SelectedResource.ToString());
    }

    public async Task RestorePanelState()
    {
        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        Guard.IsNotNull(workspaceSettings);

        var resource = await workspaceSettings.GetPropertyAsync<string>(PreviousSelectedResourceKey);
        if (string.IsNullOrEmpty(resource))
        {
            return;
        }

        // Use ExecuteImmediate() to ensure the command is executed while the workspace is still loading.
        var selectResult = await _commandService.ExecuteImmediate<ISelectResourceCommand>(command =>
        {
            command.Resource = resource;
        });

        if (selectResult.IsFailure)
        {
            _logger.LogWarning(selectResult, $"Failed to select previously selected resource '{resource}'");
        }
    }

    public async Task<Result> OpenFileManager(ResourceKey resource)
    {
        var path = ResourceRegistry.GetResourcePath(resource);
        var openResult = await ResourceUtils.OpenFileManager(path);
        if (openResult.IsFailure)
        {
            return Result.Fail($"Failed to open file manager for resource: {resource}")
                .WithErrors(openResult);
        }

        return Result.Ok();
    }

    public async Task<Result> OpenApplication(ResourceKey resource)
    {
        var path = ResourceRegistry.GetResourcePath(resource);
        var openResult = await ResourceUtils.OpenApplication(path);
        if (openResult.IsFailure)
        {
            return Result.Fail($"Failed to open associated application for resource: {resource}")
                .WithErrors(openResult);
        }

        return Result.Ok();
    }

    public async Task<Result> OpenBrowser(string url)
    {
        var openResult = await ResourceUtils.OpenBrowser(url);
        if (openResult.IsFailure)
        {
            return Result.Fail($"Failed to open url in system default browser: {url}")
                .WithErrors(openResult);
        }

        return Result.Ok();
    }

    public IconDefinition GetIconForResource(ResourceKey resource)
    {
        // If the resource is a folder, use the folder icon
        var getResourceResult = ResourceRegistry.GetResource(resource);
        if (getResourceResult.IsSuccess)
        {
            var r = getResourceResult.Value;
            if (r is IFolderResource)
            {
                var icon = _iconService.DefaultFolderIcon with
                {
                    // Todo: Define this color in resources
                    FontColor = "#FFCC40"
                };
                return icon;
            }
        }

        // If the resource is a file, use the icon matching the file extension
        var fileExtension = Path.GetExtension(resource);
        var getIconResult = _iconService.GetIconForFileExtension(fileExtension);
        if (getIconResult.IsSuccess)
        {
            return getIconResult.Value;
        }

        // Return the default file icon if we couldn't find a better match
        return _iconService.DefaultFileIcon;
    }

    public void OpenResource(ResourceKey resource)
    {
        var fileExtension = Path.GetExtension(resource.ResourceName);

        if (fileExtension == ExplorerConstants.WebAppExtension)
        {
            var webFilePath = ResourceRegistry.GetResourcePath(resource);

            var extractResult = ResourceUtils.ExtractUrlFromWebAppFile(webFilePath);
            if (extractResult.IsFailure)
            {
                _logger.LogError(extractResult.Error);
                return;
            }
            var url = extractResult.Value;

            // Execute a command to open the resource with the system default browser
            _commandService.Execute<IOpenBrowserCommand>(command =>
            {
                command.URL = url;
            });
        }
        else
        {
            // Execute a command to open the resource with the associated application
            _commandService.Execute<IOpenApplicationCommand>(command =>
            {
                command.Resource = resource;
            });
        }
    }

    private bool PathContainsSubPath(string path, string subPath)
    {
        string pathA = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string pathB = Path.GetFullPath(subPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return pathA.StartsWith(pathB, StringComparison.OrdinalIgnoreCase);
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed objects here
                _messengerService.UnregisterAll(this);
                
                // Shutdown and dispose the resource change monitor
                _resourceChangeMonitor?.Shutdown();
                _resourceChangeMonitor?.Dispose();
            }

            _disposed = true;
        }
    }

    ~ExplorerService()
    {
        Dispose(false);
    }
}
