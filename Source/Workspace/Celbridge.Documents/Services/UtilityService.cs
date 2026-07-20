using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.UserInterface;
using Celbridge.Workspace;

namespace Celbridge.Documents.Services;

public class UtilityService : IUtilityService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<UtilityService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly UtilityResourceSeeder _utilityResourceSeeder;

    private readonly List<CustomUtilityView> _utilities = new();

    private bool _disposed;

    private IDocumentsPanel DocumentsPanel => _workspaceWrapper.WorkspaceService.DocumentsPanel;

    public UtilityService(
        IServiceProvider serviceProvider,
        ILogger<UtilityService> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        _serviceProvider = serviceProvider;
        _logger = logger;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;

        _utilityResourceSeeder = new UtilityResourceSeeder(
            _workspaceWrapper,
            serviceProvider.GetRequiredService<ILogger<UtilityResourceSeeder>>());
    }

    public async Task<IReadOnlyList<CustomUtility>> CreateUtilitiesAsync(IReadOnlyList<EditorInstance> instances)
    {
        var localizationService = _serviceProvider.GetRequiredService<IPackageLocalizationService>();

        var tabs = new List<CustomUtility>();
        foreach (var instance in instances)
        {
            var contribution = instance.Contribution;
            var descriptor = contribution.UtilityDescriptor;
            if (descriptor is null)
            {
                continue;
            }

            var utilityId = instance.InstanceId;

            // Each utility owns one state file, named from its contribution reference.
            var resourceValue = $"{ProjectConstants.UtilsFolder}:{utilityId}{descriptor.ResourceExtension}";
            if (!ResourceKey.TryCreate(resourceValue, out var resource))
            {
                _logger.LogError($"Utility '{utilityId}' has an invalid backing resource: '{resourceValue}'");
                continue;
            }

            var seedResult = await _utilityResourceSeeder.SeedIfMissingAsync(resource, contribution);
            if (seedResult.IsFailure)
            {
                _logger.LogError(seedResult, $"Failed to seed utility backing file: '{resource}'");
                continue;
            }

            var displayName = ResolveLocalizedString(localizationService, contribution.Package, contribution.DisplayName);

            var panelView = _serviceProvider.GetRequiredService<CustomUtilityView>();
            var bindResult = await panelView.BindAsync(instance, resource, displayName);
            if (bindResult.IsFailure)
            {
                _logger.LogError(bindResult, $"Failed to bind utility: '{resource}'");
                continue;
            }

            // A lazy-load utility defers its WebView to the first show; every other utility
            // initializes now.
            if (!descriptor.LazyLoad)
            {
                var initResult = await panelView.EnsureInitializedAsync();
                if (initResult.IsFailure)
                {
                    _logger.LogError(initResult, $"Failed to initialize utility: '{resource}'");
                    continue;
                }
            }

            _utilities.Add(panelView);

            var icon = descriptor.Icon;
            var tooltip = ResolveLocalizedString(localizationService, contribution.Package, descriptor.Tooltip);
            tabs.Add(new CustomUtility(utilityId, icon, tooltip, displayName, panelView, panelView.FocusPanel));
        }

        return tabs;
    }

    public async Task<Result> EnsureUtilityInitializedAsync(EditorInstanceId utilityId)
    {
        var panelView = _utilities.FirstOrDefault(utility => utility.UtilityId == utilityId);
        if (panelView is null)
        {
            // Built-in utilities and unknown ids have no deferred initialization.
            return Result.Ok();
        }

        var initResult = await panelView.EnsureInitializedAsync();
        if (initResult.IsFailure)
        {
            _logger.LogError(initResult, $"Failed to initialize utility: '{utilityId}'");
        }

        return initResult;
    }

    public async Task<Result> RestoreDockedUtility(ResourceKey resource, DocumentAddress address)
    {
        var panelView = _utilities.FirstOrDefault(utility => utility.FileResource == resource);
        if (panelView is null)
        {
            // The utility no longer exists: its package or instance declaration was removed since
            // the layout was saved.
            return Result.Fail($"Cannot restore docked utility: no utility found for resource '{resource}'");
        }

        if (panelView.Location == DockLocation.Document)
        {
            // Defensive: a resource should appear at most once in the stored layout.
            return Result.Ok();
        }

        // A lazy utility restored into the tab layout as a docked document initializes at restore.
        var initResult = await panelView.EnsureInitializedAsync();
        if (initResult.IsFailure)
        {
            return Result.Fail($"Failed to initialize docked utility for resource '{resource}'")
                .WithErrors(initResult);
        }

        // Restore into the saved section and tab position without activating, because the active document is
        // restored separately. No flash and no rail navigation either, both of which belong to the interactive
        // dock only.
        var documentsPanel = (DocumentsPanel)DocumentsPanel;
        var placement = new DockUtilityPlacement(address, Activate: false);
        var dockResult = documentsPanel.DockUtility(panelView, placement);
        if (dockResult.IsFailure)
        {
            return Result.Fail($"Failed to restore docked utility for resource '{resource}'")
                .WithErrors(dockResult);
        }

        panelView.Location = DockLocation.Document;

        // Mark the rail button as a document so its click activates the tab and its cue shows, matching a live dock.
        _workspaceWrapper.WorkspaceService.UtilityPanel.SetUtilityDockLocation(
            panelView.UtilityId, DockLocation.Document, resource);

        return Result.Ok();
    }

    public bool HasUtility(EditorInstanceId utilityId)
    {
        return _utilities.Any(utility => utility.UtilityId == utilityId);
    }

    public async Task<Result> DockUtilityAsync(EditorInstanceId utilityId, DockLocation location)
    {
        var panelView = _utilities.FirstOrDefault(utility => utility.UtilityId == utilityId);
        if (panelView is null)
        {
            return Result.Fail($"Cannot dock utility: no utility found for '{utilityId}'");
        }

        // Docking presents the utility, so a lazy utility initializes here.
        var initResult = await panelView.EnsureInitializedAsync();
        if (initResult.IsFailure)
        {
            return Result.Fail($"Failed to initialize utility '{utilityId}' for docking")
                .WithErrors(initResult);
        }

        if (location == DockLocation.Document)
        {
            return DockUtilityAsDocument(panelView);
        }

        if (location == DockLocation.UtilityPanel)
        {
            return DockUtilityInPanel(panelView);
        }

        return Result.Fail($"Cannot dock utility '{utilityId}': unknown dock location '{location}'");
    }

    // Docks a utility into a document tab in the active document's section, reusing its live WebView. Activates
    // the tab if the utility is already there.
    private Result DockUtilityAsDocument(CustomUtilityView panelView)
    {
        var documentsPanel = (DocumentsPanel)DocumentsPanel;

        if (panelView.Location == DockLocation.Document)
        {
            documentsPanel.ActivateUtilityTab(panelView.FileResource);
            FlashDocumentTab(panelView.FileResource);
            return Result.Ok();
        }

        // A null address docks into the active document's section and activates the tab.
        var placement = new DockUtilityPlacement(Address: null, Activate: true);
        var dockResult = documentsPanel.DockUtility(panelView, placement);
        if (dockResult.IsFailure)
        {
            return Result.Fail($"Failed to dock utility '{panelView.UtilityId}' as a document")
                .WithErrors(dockResult);
        }

        panelView.Location = DockLocation.Document;

        var utilityPanel = _workspaceWrapper.WorkspaceService.UtilityPanel;

        // The utility's surface has left the Utility Panel, so show Explorer so the panel is not left blank.
        utilityPanel.ShowUtility(BuiltInUtilityIds.Explorer);

        // Tell the rail this utility is a document, so its button dims and its click activates the tab.
        utilityPanel.SetUtilityDockLocation(panelView.UtilityId, DockLocation.Document, panelView.FileResource);

        FlashDocumentTab(panelView.FileResource);

        return Result.Ok();
    }

    // Docks a utility back into the Utility Panel, reparenting its WebView out of its document tab and removing
    // the tab. The utility itself is never torn down.
    private Result DockUtilityInPanel(CustomUtilityView panelView)
    {
        if (panelView.Location == DockLocation.UtilityPanel)
        {
            return Result.Ok();
        }

        // Reparent the controller's WebView back to the Utility Panel before the tab is removed, so the WebView
        // is never orphaned with the discarded tab.
        panelView.Controller.Redock(panelView.PanelContainer, panelView.PanelFocusContext);
        panelView.Location = DockLocation.UtilityPanel;

        var documentsPanel = (DocumentsPanel)DocumentsPanel;
        documentsPanel.RemoveUtilityTab(panelView.FileResource);

        var utilityPanel = _workspaceWrapper.WorkspaceService.UtilityPanel;
        utilityPanel.SetUtilityDockLocation(panelView.UtilityId, DockLocation.UtilityPanel, ResourceKey.Empty);

        // Flash the freed rail button so its now-available home is obvious.
        utilityPanel.FlashUtility(panelView.UtilityId);

        return Result.Ok();
    }

    public EditorInstanceId? GetDockedUtilityId(ResourceKey resource)
    {
        var panelView = _utilities.FirstOrDefault(utility => utility.Location == DockLocation.Document
            && utility.FileResource == resource);

        return panelView?.UtilityId;
    }

    // Requests a brief attention flash on a docked utility's tab.
    private void FlashDocumentTab(ResourceKey fileResource)
    {
        _messengerService.Send(new FlashDocumentMessage(fileResource));
    }

    // Ticks each utility's save timer and flushes the ones that are due. A save failure on a writable utility
    // is logged. The expected read-only failure is suppressed so a locked backing file does not spam the log
    // on every tick.
    public async Task SaveModifiedUtilities(double deltaTime)
    {
        foreach (var utility in _utilities)
        {
            if (!utility.HasUnsavedChanges)
            {
                continue;
            }

            var updateResult = utility.UpdateSaveTimer(deltaTime);
            if (updateResult.IsFailure)
            {
                continue;
            }

            var shouldSave = updateResult.Value;
            if (!shouldSave)
            {
                continue;
            }

            var saveResult = await utility.SaveAsync();
            if (saveResult.IsFailure
                && utility.WritableState == WritableState.Writable)
            {
                _logger.LogError(saveResult, $"Failed to save utility: '{utility.FileResource}'");
            }
        }
    }

    public async Task TeardownUtilitiesAsync()
    {
        foreach (var utility in _utilities)
        {
            try
            {
                if (utility.HasUnsavedChanges)
                {
                    await utility.SaveAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to flush utility during teardown");
            }

            utility.Teardown();
        }

        _utilities.Clear();
    }

    private static string ResolveLocalizedString(IPackageLocalizationService localizationService, PackageInfo package, string key)
    {
        var localizationStrings = localizationService.LoadStrings(package);
        if (localizationStrings.TryGetValue(key, out var localized))
        {
            return localized;
        }

        return key;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        // Defensive: the unload path calls TeardownUtilitiesAsync first, which clears the list, so this
        // normally does nothing.
        foreach (var utility in _utilities)
        {
            utility.Teardown();
        }
        _utilities.Clear();
    }
}
