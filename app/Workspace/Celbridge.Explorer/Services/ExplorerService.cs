using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.UserInterface;
using Celbridge.Workspace;

namespace Celbridge.Explorer.Services;

public class ExplorerService : IExplorerService, IDisposable
{
    private const string PreviousSelectedResourcesKey = "PreviousSelectedResources";

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

    public IFolderStateService FolderStateService { get; }

    public ResourceKey SelectedResource { get; private set; }

    public List<ResourceKey> SelectedResources => ExplorerPanel?.GetSelectedResources() ?? [];

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

        FolderStateService = serviceProvider.GetRequiredService<IFolderStateService>();

        _messengerService.Register<WorkspaceWillPopulatePanelsMessage>(this, OnWorkspaceWillPopulatePanelsMessage);
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoadedMessage);
        _messengerService.Register<SelectedResourceChangedMessage>(this, OnSelectedResourceChangedMessage);
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
            _ = StoreSelectedResources();
        }
    }

    public async Task<Result> SelectResource(ResourceKey resource)
    {
        Guard.IsNotNull(ExplorerPanel);

        var selectResult = await ExplorerPanel.SelectResource(resource);
        if (selectResult.IsFailure)
        {
            return Result.Fail($"Failed to select resource: {resource}")
                .WithErrors(selectResult);
        }

        return Result.Ok();
    }

    public async Task StoreSelectedResources()
    {
        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        Guard.IsNotNull(workspaceSettings);

        // Store all selected resources as a JSON array
        var resourceStrings = SelectedResources.Select(r => r.ToString()).ToList();
        var json = JsonSerializer.Serialize(resourceStrings);
        await workspaceSettings.SetPropertyAsync(PreviousSelectedResourcesKey, json);
    }

    public async Task RestorePanelState()
    {
        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        Guard.IsNotNull(workspaceSettings);

        var json = await workspaceSettings.GetPropertyAsync<string>(PreviousSelectedResourcesKey);
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        try
        {
            var resourceStrings = JsonSerializer.Deserialize<List<string>>(json);
            if (resourceStrings == null || resourceStrings.Count == 0)
            {
                return;
            }

            var resources = resourceStrings.Select(s => new ResourceKey(s)).ToList();

            // Select all previously selected resources
            Guard.IsNotNull(ExplorerPanel);
            var selectResult = await ExplorerPanel.SelectResources(resources);
            if (selectResult.IsFailure)
            {
                _logger.LogWarning(selectResult, $"Failed to restore previously selected resources");
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning($"Failed to deserialize previously selected resources: {ex.Message}");
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
