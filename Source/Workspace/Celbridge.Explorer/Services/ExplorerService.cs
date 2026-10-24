using System.Text.Json;
using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Explorer.Services;

public class ExplorerService : IExplorerService, IDisposable
{
    private const string PreviousSelectedResourcesKey = "PreviousSelectedResources";

    private readonly ILogger<ExplorerService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IIconService _iconService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IFileManagerLauncher _fileManagerLauncher;
    private readonly ISpotlightService _spotlightService;
    private readonly ISpotlightLandmark _spotlightLandmark;

    // The Explorer panel landmark.
    private const string ExplorerPanelLandmarkId = "explorer-panel";

    // The Explorer toolbar button landmarks.
    private static readonly string[] ToolbarLandmarkIds =
    {
        "new-file-button",
        "new-folder-button",
        "collapse-folders-button",
    };

    private IRootHandlerRegistry RootHandlers =>
        _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;

    public IFolderStateService FolderStateService { get; }

    public ResourceKey SelectedResource { get; private set; }

    private List<ResourceKey> _selectedResources = [];
    public List<ResourceKey> SelectedResources => _selectedResources;

    private bool _isWorkspaceLoaded;

    public ExplorerService(
        IServiceProvider serviceProvider,
        ILogger<ExplorerService> logger,
        IMessengerService messengerService,
        IIconService iconService,
        IWorkspaceWrapper workspaceWrapper,
        IFileManagerLauncher fileManagerLauncher,
        ISpotlightService spotlightService)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspaceLoaded);

        _logger = logger;
        _messengerService = messengerService;
        _iconService = iconService;
        _workspaceWrapper = workspaceWrapper;
        _fileManagerLauncher = fileManagerLauncher;
        _spotlightService = spotlightService;

        FolderStateService = serviceProvider.GetRequiredService<IFolderStateService>();

        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoadedMessage);
        _messengerService.Register<SelectedResourceChangedMessage>(this, OnSelectedResourceChangedMessage);

        // Register the Explorer reveal so spotlighting an Explorer landmark switches to the Explorer tab
        // first (its content is collapsed while another activity is active). The reveal is stateless, so the
        // panel and toolbar landmarks share one instance. Torn down when this workspace-scoped service is disposed.
        _spotlightLandmark = new ExplorerSpotlightLandmark(workspaceWrapper);
        _spotlightService.RegisterLandmark(ExplorerPanelLandmarkId, _spotlightLandmark);
        foreach (var landmarkId in ToolbarLandmarkIds)
        {
            _spotlightService.RegisterLandmark(landmarkId, _spotlightLandmark);
        }
    }

    private void OnWorkspaceLoadedMessage(object recipient, WorkspaceLoadedMessage message)
    {
        // Once set, this will remain true for the lifetime of the service
        _isWorkspaceLoaded = true;
    }

    private void OnSelectedResourceChangedMessage(object recipient, SelectedResourceChangedMessage message)
    {
        SelectedResource = message.Resource;

        // Update the selected resources list from the panel
        var explorerPanel = _workspaceWrapper.WorkspaceService.UtilityPanel.ExplorerPanel;
        _selectedResources = explorerPanel.GetSelectedResources();

        if (_isWorkspaceLoaded)
        {
            // Ignore change events that happen while loading the workspace
            _ = StoreSelectedResources();
        }
    }

    public async Task<Result> SelectResources(List<ResourceKey> resources)
    {
        var explorerPanel = _workspaceWrapper.WorkspaceService.UtilityPanel.ExplorerPanel;

        var selectResult = await explorerPanel.SelectResources(resources);
        if (selectResult.IsFailure)
        {
            return Result.Fail($"Failed to select resources")
                .WithErrors(selectResult);
        }

        return Result.Ok();
    }

    public async Task StoreSelectedResources()
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.WorkspaceSettings.PropertyBag;
        Guard.IsNotNull(propertyBag);

        // Store all selected resources as a JSON array
        var resourceStrings = SelectedResources.Select(r => r.ToString()).ToList();
        var json = JsonSerializer.Serialize(resourceStrings);
        await propertyBag.SetPropertyAsync(PreviousSelectedResourcesKey, json);
    }

    public async Task RestorePanelState()
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.WorkspaceSettings.PropertyBag;
        Guard.IsNotNull(propertyBag);

        // Lists the project folder and the expanded folders the workspace saved.
        var explorerPanel = _workspaceWrapper.WorkspaceService.UtilityPanel.ExplorerPanel;
        await explorerPanel.RefreshTreeAsync();

        var json = await propertyBag.GetPropertyAsync<string>(PreviousSelectedResourcesKey);
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

            var resources = resourceStrings
                .Where(s => ResourceKey.TryCreate(s, out _))
                .Select(s => ResourceKey.Create(s))
                .ToList();

            // Select all previously selected resources
            var selectResult = await explorerPanel.SelectResources(resources);
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
        var resolveResult = RootHandlers.ResolveResourcePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var openResult = await _fileManagerLauncher.OpenFileManagerAsync(resolveResult.Value);
        if (openResult.IsFailure)
        {
            return Result.Fail($"Failed to open file manager for resource: {resource}")
                .WithErrors(openResult);
        }

        return Result.Ok();
    }

    public async Task<Result> OpenApplication(ResourceKey resource)
    {
        var resolveResult = RootHandlers.ResolveResourcePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var openResult = await _fileManagerLauncher.OpenApplicationAsync(resolveResult.Value);
        if (openResult.IsFailure)
        {
            return Result.Fail($"Failed to open associated application for resource: {resource}")
                .WithErrors(openResult);
        }

        return Result.Ok();
    }

    public IconDefinition GetIconForFile(ResourceKey file)
    {
        // Match on the whole name so a file with no extension is recognised.
        var getIconResult = _iconService.GetFileIconForFileName(file.ResourceName);
        if (getIconResult.IsSuccess)
        {
            return getIconResult.Value;
        }

        return _iconService.DefaultFileIcon;
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

                _spotlightService.UnregisterLandmark(ExplorerPanelLandmarkId);
                foreach (var landmarkId in ToolbarLandmarkIds)
                {
                    _spotlightService.UnregisterLandmark(landmarkId);
                }
            }

            _disposed = true;
        }
    }

    ~ExplorerService()
    {
        Dispose(false);
    }
}
