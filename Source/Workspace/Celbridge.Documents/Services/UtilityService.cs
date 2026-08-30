using Celbridge.Commands;
using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workshop;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Services;

public class UtilityService : IUtilityService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<UtilityService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly UtilityResourceSeeder _utilityResourceSeeder;

    private readonly List<CustomUtilityView> _utilities = new();

    // Spotlight landmark ids for the launcher rail buttons. These must match the descriptors seeded in
    // SpotlightLandmarks exactly.
    private const string ProjectSettingsLandmarkId = "project-settings-utility-button";
    private const string WorkshopLandmarkId = "workshop-utility-button";

    // The rail register, in rail order. The built-in utilities are published by the Utility Panel because
    // their descriptors wrap live views. The rest are built here.
    private readonly List<UtilityRailItem> _builtInUtilityItems = new();
    private readonly List<UtilityRailItem> _contributedItems = new();
    private readonly List<UtilityRailItem> _builtInLauncherItems = new();

    private bool _disposed;

    private IDocumentsPanel DocumentsPanel => _workspaceWrapper.WorkspaceService.DocumentsPanel;

    public UtilityService(
        IServiceProvider serviceProvider,
        ILogger<UtilityService> logger,
        IMessengerService messengerService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspaceLoaded);

        _serviceProvider = serviceProvider;
        _logger = logger;
        _messengerService = messengerService;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;

        _utilityResourceSeeder = new UtilityResourceSeeder(
            _workspaceWrapper,
            serviceProvider.GetRequiredService<ILogger<UtilityResourceSeeder>>());
    }

    public void RegisterBuiltInUtilityItems(IReadOnlyList<UtilityRailItem> builtInUtilityItems)
    {
        _builtInUtilityItems.Clear();
        _builtInUtilityItems.AddRange(builtInUtilityItems);
    }

    public IReadOnlyList<UtilityRailItem> GetRailItems()
    {
        var railItems = new List<UtilityRailItem>();
        railItems.AddRange(_builtInUtilityItems);
        railItems.AddRange(_contributedItems);
        railItems.AddRange(_builtInLauncherItems);

        return railItems;
    }

    public WorkspaceArea? GetCurrentArea(EditorId itemId)
    {
        // A utility carries its own area because it moves.
        var utility = _utilities.FirstOrDefault(candidate => candidate.UtilityId == itemId);
        if (utility is not null)
        {
            return utility.Area;
        }

        foreach (var railItem in GetRailItems())
        {
            if (railItem.ItemId != itemId)
            {
                continue;
            }

            switch (railItem.Kind)
            {
                case RailItemKind.PanelUtility:
                case RailItemKind.DockableUtility:
                    // Explorer and Search are registered without ever entering _utilities, because the
                    // panel owns their views. The panel is where they are.
                    return WorkspaceArea.Utility;

                case RailItemKind.DocumentLauncher:
                    // A launcher's document sits wherever the user last moved its tab. Closed, it occupies
                    // no area at all, and DockArea is what says where it would open.
                    return FindOpenDocumentArea(railItem.FileResource);

                default:
                    throw new NotSupportedException($"Unhandled rail item kind '{railItem.Kind}'");
            }
        }

        return null;
    }

    // The area holding the open document for a resource, or null when no document is open for it.
    private WorkspaceArea? FindOpenDocumentArea(ResourceKey resource)
    {
        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;

        var openDocument = documentsService.FindOpenDocument(resource);
        if (openDocument is null)
        {
            return null;
        }

        return openDocument.Address.Section.GetArea().GetWorkspaceArea();
    }

    public async Task CreateUtilitiesAsync(IReadOnlyList<ResolvedEditor> resolvedEditors)
    {
        var localizationService = _serviceProvider.GetRequiredService<IPackageLocalizationService>();

        _contributedItems.Clear();
        foreach (var resolvedEditor in resolvedEditors)
        {
            var contribution = resolvedEditor.Contribution;
            var descriptor = contribution.UtilityDescriptor;
            if (descriptor is null)
            {
                continue;
            }

            var utilityId = resolvedEditor.EditorId;

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

            var displayName = PackageDisplayText.Resolve(localizationService, contribution.Package, contribution.DisplayName);
            var tooltip = PackageDisplayText.Resolve(localizationService, contribution.Package, contribution.Description);

            var panelViewResult = await CreateUtilityViewAsync(resolvedEditor, resource, displayName);
            if (panelViewResult.IsFailure)
            {
                _logger.LogError(panelViewResult, $"Failed to create utility: '{resource}'");
                continue;
            }
            var panelView = panelViewResult.Value;

            _utilities.Add(panelView);

            var landmarkId = $"{utilityId}-utility-button";
            var railPanelView = new UtilityRailPanelView(panelView, panelView.FocusPanel, FocusPanelId.CustomUtility);

            UtilityRailItem railItem;
            if (descriptor.DockArea is null)
            {
                railItem = UtilityRailItem.CreatePanelUtility(
                    utilityId, landmarkId, descriptor.Icon, displayName, tooltip, railPanelView,
                    resource, resolvedEditor.EditorId);
            }
            else
            {
                railItem = UtilityRailItem.CreateDockableUtility(
                    utilityId, landmarkId, descriptor.Icon, displayName, tooltip,
                    resource, resolvedEditor.EditorId, railPanelView, descriptor.DockArea.Value);
            }

            _contributedItems.Add(railItem);
        }

        _builtInLauncherItems.Clear();
        _builtInLauncherItems.AddRange(BuildBuiltInLauncherItems());
    }

    // Builds a workspace-scoped utility's persistent view: the one the Utility Panel hosts and the dock
    // orchestration reparents. Its WebView is created here, so it is ready wherever the utility is presented.
    private async Task<Result<CustomUtilityView>> CreateUtilityViewAsync(
        ResolvedEditor resolvedEditor,
        ResourceKey resource,
        string displayName)
    {
        var panelView = _serviceProvider.GetRequiredService<CustomUtilityView>();

        var bindResult = await panelView.BindAsync(resolvedEditor, resource, displayName);
        if (bindResult.IsFailure)
        {
            return Result<CustomUtilityView>.Fail($"Failed to bind utility: '{resource}'")
                .WithErrors(bindResult);
        }

        return panelView;
    }

    // The built-in launchers: rail items that open a document and never occupy the panel, so they carry no
    // panel view. A contribution declaring no utility area builds the same shape from its manifest.
    private List<UtilityRailItem> BuildBuiltInLauncherItems()
    {
        var projectService = _serviceProvider.GetRequiredService<IProjectService>();
        var workshopService = _serviceProvider.GetRequiredService<IWorkshopService>();
        var stringLocalizer = _serviceProvider.GetRequiredService<IStringLocalizer>();
        var iconService = _serviceProvider.GetRequiredService<IIconService>();

        var launcherItems = new List<UtilityRailItem>();

        // The project file sits at the project root, so its resource key is just the file name. The editor is
        // named so the choice does not depend on extension resolution.
        var project = projectService.CurrentProject;
        if (project is not null)
        {
            var projectFileName = Path.GetFileName(project.ProjectFilePath);
            if (ResourceKey.TryCreate(projectFileName, out var projectFileResource))
            {
                string projectSettingsName = stringLocalizer.GetString("UtilityPanel_ProjectSettingsTooltip");

                var projectSettingsItem = UtilityRailItem.CreateDocumentLauncher(
                    BuiltInLauncherIds.ProjectSettings,
                    ProjectSettingsLandmarkId,
                    iconService.GetIconName(IconSymbol.Sliders),
                    projectSettingsName,
                    projectSettingsName,
                    projectFileResource,
                    BuiltInEditors.ProjectSettingsEditorId,
                    WorkspaceArea.Main);

                launcherItems.Add(projectSettingsItem);
            }
        }

        string workshopName = stringLocalizer.GetString("UtilityPanel_WorkshopTooltip");

        var workshopItem = UtilityRailItem.CreateDocumentLauncher(
            BuiltInLauncherIds.Workshop,
            WorkshopLandmarkId,
            iconService.GetIconName(IconSymbol.People),
            workshopName,
            workshopName,
            workshopService.DocumentResource,
            BuiltInEditors.WebViewEditorId,
            WorkspaceArea.Main);

        launcherItems.Add(workshopItem);

        return launcherItems;
    }

    public async Task<Result> RestoreDockedUtility(ResourceKey resource, DocumentAddress address)
    {
        var panelView = _utilities.FirstOrDefault(utility => utility.FileResource == resource);
        if (panelView is null)
        {
            // The utility no longer exists: its package or contribution declaration was removed since
            // the layout was saved.
            return Result.Fail($"Cannot restore docked utility: no utility found for resource '{resource}'");
        }

        if (panelView.Area != WorkspaceArea.Utility)
        {
            // Defensive: a resource should appear at most once in the stored layout.
            return Result.Ok();
        }

        var placement = new DockUtilityPlacement(address.Section, address.TabOrder, Activate: false);

        // A restore never activates, because the active document is restored separately, and never flashes
        // or navigates the rail, both of which belong to the interactive dock.
        var documentsPanel = (WorkspacePanel)DocumentsPanel;
        var dockResult = documentsPanel.DockUtility(panelView, placement);
        if (dockResult.IsFailure)
        {
            return Result.Fail($"Failed to restore docked utility for resource '{resource}'")
                .WithErrors(dockResult);
        }

        panelView.Area = placement.Section.GetArea().GetWorkspaceArea();

        // Mark the rail button as a document so its click activates the tab and its cue shows, matching a live dock.
        _workspaceWrapper.WorkspaceService.UtilityPanel.SetUtilityArea(
            panelView.UtilityId, panelView.Area, resource);

        return Result.Ok();
    }

    public bool HasUtility(EditorId utilityId)
    {
        return _utilities.Any(utility => utility.UtilityId == utilityId);
    }

    public async Task<Result> DockUtilityAsync(EditorId utilityId, WorkspaceArea area)
    {
        var panelView = _utilities.FirstOrDefault(utility => utility.UtilityId == utilityId);
        if (panelView is null)
        {
            return Result.Fail($"Cannot dock utility: no utility found for '{utilityId}'");
        }

        // The Utility Panel is the one area that holds no document area.
        var dockArea = area.GetDocumentArea();
        if (dockArea is null)
        {
            return DockUtilityInPanel(panelView);
        }

        var railItem = FindUtilityItem(utilityId);
        if (railItem is not null
            && railItem.Kind == RailItemKind.PanelUtility)
        {
            return Result.Fail(
                $"Cannot dock utility '{utilityId}' in the '{area.ToToken()}' area: " +
                $"it stays in the Utility Panel.");
        }

        return DockUtilityAsDocument(panelView, dockArea.Value);
    }

    // The rail item holding a utility's declaration. Null for an id the register does not hold, which
    // cannot happen for a live utility.
    private UtilityRailItem? FindUtilityItem(EditorId utilityId)
    {
        return _contributedItems.FirstOrDefault(item => item.ItemId == utilityId);
    }

    // Docks a utility into a document tab in the area's primary section, reusing its live WebView. A utility
    // already docked in another document area moves. One already in this area is activated in place.
    private Result DockUtilityAsDocument(CustomUtilityView panelView, DocumentArea dockArea)
    {
        var documentsPanel = (WorkspacePanel)DocumentsPanel;
        var area = dockArea.GetWorkspaceArea();

        if (panelView.Area == area)
        {
            documentsPanel.ActivateUtilityTab(panelView.FileResource);
            PresentArea(area);
            FlashDocumentTab(panelView.FileResource);
            return Result.Ok();
        }

        // A utility docked in another document area returns to the panel first, so its WebView is reparented
        // out of the old tab before the new one is built.
        if (panelView.Area != WorkspaceArea.Utility)
        {
            ReturnUtilityToPanel(panelView);
        }

        var section = dockArea.GetPrimarySection();
        var placement = new DockUtilityPlacement(section, TabOrder: null, Activate: true);
        var dockResult = documentsPanel.DockUtility(panelView, placement);
        if (dockResult.IsFailure)
        {
            return Result.Fail($"Failed to dock utility '{panelView.UtilityId}' as a document")
                .WithErrors(dockResult);
        }

        panelView.Area = area;

        var utilityPanel = _workspaceWrapper.WorkspaceService.UtilityPanel;

        utilityPanel.SetUtilityArea(panelView.UtilityId, area, panelView.FileResource);

        PresentArea(area);

        FlashDocumentTab(panelView.FileResource);

        return Result.Ok();
    }

    // Reveals a collapsed area, so docking into one presents the utility rather than hiding it. Main is never
    // collapsed, which is the no-op case.
    private void PresentArea(WorkspaceArea area)
    {
        if (!area.IsCollapsible())
        {
            return;
        }

        _commandService.Execute<ISetAreaVisibilityCommand>(command =>
        {
            command.Area = area;
            command.IsVisible = true;
        });
    }

    // Docks a utility back into the Utility Panel and shows it there. The utility itself is never torn down.
    private Result DockUtilityInPanel(CustomUtilityView panelView)
    {
        // Already in the panel, docking is only a reveal. The panel still has to be presented, because a
        // caller that docks without going on to show gets no other chance to.
        if (panelView.Area == WorkspaceArea.Utility)
        {
            PresentArea(WorkspaceArea.Utility);
            return Result.Ok();
        }

        ReturnUtilityToPanel(panelView);

        // Present the utility at its destination, mirroring the dock as a document, which activates the tab.
        _workspaceWrapper.WorkspaceService.UtilityPanel.ShowUtility(panelView.UtilityId);

        return Result.Ok();
    }

    // Reparents a docked utility's WebView back into the Utility Panel and drops its document tab, without
    // showing it. The reparent runs before the tab is removed so the WebView is never orphaned with the
    // discarded tab.
    private void ReturnUtilityToPanel(CustomUtilityView panelView)
    {
        panelView.Controller.Redock(panelView.PanelContainer, panelView.PanelFocusContext);
        panelView.Area = WorkspaceArea.Utility;

        var documentsPanel = (WorkspacePanel)DocumentsPanel;
        documentsPanel.RemoveUtilityTab(panelView.FileResource);

        var utilityPanel = _workspaceWrapper.WorkspaceService.UtilityPanel;
        utilityPanel.SetUtilityArea(panelView.UtilityId, WorkspaceArea.Utility, ResourceKey.Empty);
    }

    public EditorId? GetDockedUtilityId(ResourceKey resource)
    {
        var panelView = _utilities.FirstOrDefault(utility => utility.Area != WorkspaceArea.Utility
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
