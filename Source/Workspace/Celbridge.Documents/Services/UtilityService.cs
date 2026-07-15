using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Packages;
using Celbridge.UserInterface;
using Celbridge.Workspace;

namespace Celbridge.Documents.Services;

public class UtilityService : IUtilityService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<UtilityService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly UtilityDocumentSeeder _utilityDocumentSeeder;

    private readonly List<ContributionPanelView> _utilities = new();

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

        _utilityDocumentSeeder = new UtilityDocumentSeeder(
            _workspaceWrapper,
            serviceProvider.GetRequiredService<ILogger<UtilityDocumentSeeder>>());
    }

    public async Task<IReadOnlyList<ContributedUtility>> CreateUtilitiesAsync(IReadOnlyList<CustomDocumentEditorContribution> contributions)
    {
        var localizationService = _serviceProvider.GetRequiredService<IPackageLocalizationService>();

        var tabs = new List<ContributedUtility>();
        foreach (var contribution in contributions)
        {
            var descriptor = contribution.UtilityDescriptor;
            if (descriptor is null)
            {
                continue;
            }

            var utilityId = UtilityId.Create(contribution.Package.Name, contribution.Id);

            if (!ResourceKey.TryCreate(descriptor.Resource, out var resource))
            {
                _logger.LogError($"Utility '{utilityId}' declares an invalid resource: '{descriptor.Resource}'");
                continue;
            }

            var seedResult = await _utilityDocumentSeeder.SeedIfMissingAsync(contribution);
            if (seedResult.IsFailure)
            {
                _logger.LogError(seedResult, $"Failed to seed utility backing file: '{resource}'");
                continue;
            }

            var displayName = ResolveLocalizedString(localizationService, contribution.Package, contribution.DisplayName);

            var panelView = _serviceProvider.GetRequiredService<ContributionPanelView>();
            var initResult = await panelView.InitializeAsync(contribution, resource, displayName);
            if (initResult.IsFailure)
            {
                _logger.LogError(initResult, $"Failed to initialize utility: '{resource}'");
                continue;
            }

            _utilities.Add(panelView);

            var tooltip = ResolveLocalizedString(localizationService, contribution.Package, descriptor.Tooltip);
            tabs.Add(new ContributedUtility(utilityId, descriptor.Icon, tooltip, displayName, panelView, panelView.FocusPanel));
        }

        return tabs;
    }

    public Result RestoreDockedUtility(ResourceKey resource, DocumentAddress address)
    {
        var panelView = _utilities.FirstOrDefault(utility => utility.FileResource == resource);
        if (panelView is null)
        {
            // The utility no longer exists (its package was removed or disabled since the layout was saved), so
            // there is nothing to dock. The stored entry is simply dropped.
            return Result.Fail($"Cannot restore docked utility: no utility found for resource '{resource}'");
        }

        if (panelView.Location == DockLocation.Document)
        {
            // Defensive: a resource should appear at most once in the stored layout.
            return Result.Ok();
        }

        // Restore into the saved section and tab position without activating: the active document is restored
        // separately, so a docked utility must not steal activation. No flash and no rail navigation either, both
        // of which belong to the interactive dock only.
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

    public async Task<Result> DockUtilityAsync(UtilityId utilityId, DockLocation location)
    {
        await Task.CompletedTask;

        var panelView = _utilities.FirstOrDefault(utility => utility.UtilityId == utilityId);
        if (panelView is null)
        {
            return Result.Fail($"Cannot dock utility: no utility found for '{utilityId}'");
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
    private Result DockUtilityAsDocument(ContributionPanelView panelView)
    {
        var documentsPanel = (DocumentsPanel)DocumentsPanel;

        if (panelView.Location == DockLocation.Document)
        {
            // Already a document: bring its tab to the front and flash it for feedback.
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

        // Flash the newly docked tab for consistency with surfacing it from the rail.
        FlashDocumentTab(panelView.FileResource);

        return Result.Ok();
    }

    // Docks a utility back into the Utility Panel, reparenting its WebView out of its document tab and removing
    // the tab. The utility itself is never torn down.
    private Result DockUtilityInPanel(ContributionPanelView panelView)
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

        _workspaceWrapper.WorkspaceService.UtilityPanel.SetUtilityDockLocation(
            panelView.UtilityId, DockLocation.UtilityPanel, ResourceKey.Empty);

        return Result.Ok();
    }

    public UtilityId? GetDockedUtilityId(ResourceKey resource)
    {
        var panelView = _utilities.FirstOrDefault(utility => utility.Location == DockLocation.Document
            && utility.FileResource == resource);

        return panelView?.UtilityId;
    }

    // Requests a brief attention flash on a docked utility's tab. A flash is a transient view effect with no
    // state change, so it is sent as a notification for the documents panel to apply, not run as a command.
    private void FlashDocumentTab(ResourceKey fileResource)
    {
        _messengerService.Send(new FlashDocumentMessage(fileResource));
    }

    // Ticks each utility's save timer and flushes the ones that are due, mirroring the per-view save loop in
    // DocumentsPanel. A save failure on a writable utility is logged; the expected read-only failure is
    // suppressed so a locked backing file does not spam the log on every tick.
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

        // Defensive: the unload path calls TeardownUtilitiesAsync first, which clears the list, so this normally
        // does nothing. Tear down any that remain if dispose is reached by another path.
        foreach (var utility in _utilities)
        {
            utility.Teardown();
        }
        _utilities.Clear();
    }
}
