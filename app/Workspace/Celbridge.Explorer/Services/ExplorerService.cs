using Celbridge.Commands;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using Celbridge.Logging;

namespace Celbridge.Explorer.Services;

public class ExplorerService : IExplorerService, IDisposable
{
    private const string PreviousSelectedResourceKey = "PreviousSelectedResource";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExplorerService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IFileIconService _fileIconService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private IResourceRegistry? _resourceRegistry;
    private IResourceRegistry ResourceRegistry => 
        _resourceRegistry ??= _workspaceWrapper.WorkspaceService.ResourceService.Registry;

    private IExplorerPanel? _explorerPanel;
    public IExplorerPanel ExplorerPanel => _explorerPanel!;

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
        ICommandService commandService,
        IFileIconService fileIconService,
        IWorkspaceWrapper workspaceWrapper)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        _serviceProvider = serviceProvider;
        _logger = logger;
        _messengerService = messengerService;
        _commandService = commandService;
        _fileIconService = fileIconService;
        _workspaceWrapper = workspaceWrapper;

        _messengerService.Register<WorkspaceWillPopulatePanelsMessage>(this, OnWorkspaceWillPopulatePanelsMessage);
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoadedMessage);
        _messengerService.Register<SelectedResourceChangedMessage>(this, OnSelectedResourceChangedMessage);
        _messengerService.Register<ResourceRegistryUpdatedMessage>(this, OnResourceRegistryUpdatedMessage);
    }

    private void OnWorkspaceWillPopulatePanelsMessage(object recipient, WorkspaceWillPopulatePanelsMessage message)
    {
        _explorerPanel = _serviceProvider.GetRequiredService<IExplorerPanel>();
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

    private async void OnResourceRegistryUpdatedMessage(object recipient, ResourceRegistryUpdatedMessage message)
    {
        var result = await PopulateTreeViewAsync();
        if (result.IsFailure)
        {
            _logger.LogWarning(result.Error);
        }
    }

    private async Task<Result> PopulateTreeViewAsync()
    {
        var populateResult = await ResourceTreeView.PopulateTreeView(ResourceRegistry);
        if (populateResult.IsFailure)
        {
            return Result.Fail($"Failed to populate tree view. {populateResult.Error}");
        }

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

    public FileIconDefinition GetIconForResource(ResourceKey resource)
    {
        // If the resource is a folder, use the folder icon
        var getResourceResult = ResourceRegistry.GetResource(resource);
        if (getResourceResult.IsSuccess)
        {
            var r = getResourceResult.Value;
            if (r is IFolderResource)
            {
                var icon = _fileIconService.DefaultFolderIcon with
                {
                    // Todo: Define this color in resources
                    FontColor = "#FFCC40"
                };
                return icon;
            }
        }

        // If the resource is a file, use the icon matching the file extension
        var fileExtension = Path.GetExtension(resource);
        var getIconResult = _fileIconService.GetFileIconForExtension(fileExtension);
        if (getIconResult.IsSuccess)
        {
            return getIconResult.Value;
        }

        // Return the default file icon if we couldn't find a better match
        return _fileIconService.DefaultFileIcon;
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
            }

            _disposed = true;
        }
    }

    ~ExplorerService()
    {
        Dispose(false);
    }
}
