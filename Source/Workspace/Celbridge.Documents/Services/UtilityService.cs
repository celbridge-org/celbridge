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

    // A launcher opens a document and never occupies the panel.
    private static readonly IReadOnlyList<WorkspaceArea> MainOnlyAreas =
    [
        WorkspaceArea.Main
    ];

    // The rail register, in rail order. The built-in utilities are published by the Utility Panel because
    // their descriptors wrap live views; the rest are built here.
    private readonly List<UtilityRailItem> _builtInUtilityItems = new();
    private readonly List<UtilityRailItem> _utilityItems = new();
    private readonly List<UtilityRailItem> _launcherItems = new();

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
        railItems.AddRange(_utilityItems);
        railItems.AddRange(_launcherItems);

        return railItems;
    }

    public WorkspaceArea GetItemArea(EditorId itemId)
    {
        // A utility carries its own area because it moves; everything else on the rail has one place it lives,
        // which its descriptor already states.
        var utility = _utilities.FirstOrDefault(candidate => candidate.UtilityId == itemId);
        if (utility is not null)
        {
            return utility.Area;
        }

        foreach (var railItem in GetRailItems())
        {
            if (railItem.ItemId == itemId)
            {
                return railItem.DefaultArea;
            }
        }

        return WorkspaceArea.Utility;
    }

    public async Task CreateUtilitiesAsync(IReadOnlyList<ResolvedEditor> resolvedEditors)
    {
        var localizationService = _serviceProvider.GetRequiredService<IPackageLocalizationService>();

        _utilityItems.Clear();
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

            var panelView = _serviceProvider.GetRequiredService<CustomUtilityView>();
            var bindResult = await panelView.BindAsync(resolvedEditor, resource, displayName);
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

            var tooltip = PackageDisplayText.Resolve(localizationService, contribution.Package, contribution.Description);

            // A contribution utility carries both payloads: the resource it opens as a document, and the view
            // already bound to that resource for the panel to host.
            var railItem = new UtilityRailItem
            {
                ItemId = utilityId,
                LandmarkId = $"{utilityId}-utility-button",
                IconName = descriptor.Icon,
                DisplayName = displayName,
                Tooltip = tooltip,
                AllowedAreas = descriptor.AllowedAreas,
                DefaultArea = descriptor.DefaultArea,
                Resource = new UtilityRailResource(resource, resolvedEditor.EditorId),
                PanelView = new UtilityRailPanelView(panelView, panelView.FocusPanel, FocusPanelId.CustomUtility)
            };

            _utilityItems.Add(railItem);
        }

        _launcherItems.Clear();
        _launcherItems.AddRange(BuildLauncherItems());
    }

    // The launchers: rail items that open a document and never occupy the panel, so they carry no panel view.
    private List<UtilityRailItem> BuildLauncherItems()
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

                var projectSettingsItem = new UtilityRailItem
                {
                    ItemId = BuiltInLauncherIds.ProjectSettings,
                    LandmarkId = ProjectSettingsLandmarkId,
                    IconName = iconService.GetIconName(IconSymbol.Sliders),
                    DisplayName = projectSettingsName,
                    Tooltip = projectSettingsName,
                    AllowedAreas = MainOnlyAreas,
                    DefaultArea = WorkspaceArea.Main,
                    Resource = new UtilityRailResource(projectFileResource, BuiltInEditors.ProjectSettingsEditorId)
                };

                launcherItems.Add(projectSettingsItem);
            }
        }

        string workshopName = stringLocalizer.GetString("UtilityPanel_WorkshopTooltip");

        var workshopItem = new UtilityRailItem
        {
            ItemId = BuiltInLauncherIds.Workshop,
            LandmarkId = WorkshopLandmarkId,
            IconName = iconService.GetIconName(IconSymbol.People),
            DisplayName = workshopName,
            Tooltip = workshopName,
            AllowedAreas = MainOnlyAreas,
            DefaultArea = WorkspaceArea.Main,
            Resource = new UtilityRailResource(workshopService.DocumentResource, BuiltInEditors.WebViewEditorId)
        };

        launcherItems.Add(workshopItem);

        return launcherItems;
    }

    public async Task<Result> EnsureUtilityInitializedAsync(EditorId utilityId)
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
            // The utility no longer exists: its package or contribution declaration was removed since
            // the layout was saved.
            return Result.Fail($"Cannot restore docked utility: no utility found for resource '{resource}'");
        }

        if (panelView.Area != WorkspaceArea.Utility)
        {
            // Defensive: a resource should appear at most once in the stored layout.
            return Result.Ok();
        }

        var utilityId = panelView.UtilityId;

        var placementResult = ResolveRestorePlacement(utilityId, address);
        if (placementResult.IsFailure)
        {
            return Result.Fail($"Cannot restore docked utility for resource '{resource}'")
                .WithErrors(placementResult);
        }
        var placement = placementResult.Value;

        // A lazy utility restored into the tab layout as a docked document initializes at restore.
        var initResult = await panelView.EnsureInitializedAsync();
        if (initResult.IsFailure)
        {
            return Result.Fail($"Failed to initialize docked utility for resource '{resource}'")
                .WithErrors(initResult);
        }

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

    // Where a stored tab position lands on restore. The saved section and tab order are reproduced while the
    // declaration still allows that area, and a declaration that has since narrowed puts the utility at its
    // default area instead, appended, because the stored tab order belongs to another section.
    private Result<DockUtilityPlacement> ResolveRestorePlacement(EditorId utilityId, DocumentAddress address)
    {
        var allowedAreas = GetAllowedAreas(utilityId);

        var storedArea = address.Section.GetArea().GetWorkspaceArea();
        if (allowedAreas.Contains(storedArea))
        {
            return new DockUtilityPlacement(address.Section, address.TabOrder, Activate: false);
        }

        var defaultArea = GetDefaultArea(utilityId);
        var documentArea = defaultArea.GetDocumentArea();
        if (documentArea is null)
        {
            return Result.Fail(
                $"Utility '{utilityId}' no longer allows the '{storedArea.ToToken()}' area, and its default " +
                $"area is the Utility Panel.");
        }

        _logger.LogWarning(
            $"Utility '{utilityId}' no longer allows the '{storedArea.ToToken()}' area it was stored in. " +
            $"Restoring it in the '{defaultArea.ToToken()}' area instead.");

        return new DockUtilityPlacement(documentArea.Value.GetPrimarySection(), TabOrder: null, Activate: false);
    }

    private WorkspaceArea GetDefaultArea(EditorId utilityId)
    {
        var railItem = FindUtilityItem(utilityId);
        if (railItem is null)
        {
            return WorkspaceArea.Utility;
        }

        return railItem.DefaultArea;
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

        var allowedAreas = GetAllowedAreas(utilityId);
        if (!allowedAreas.Contains(area))
        {
            return Result.Fail(
                $"Cannot dock utility '{utilityId}' in the '{area.ToToken()}' area: " +
                $"it allows {DescribeAreas(allowedAreas)}.");
        }

        // Docking presents the utility, so a lazy utility initializes here.
        var initResult = await panelView.EnsureInitializedAsync();
        if (initResult.IsFailure)
        {
            return Result.Fail($"Failed to initialize utility '{utilityId}' for docking")
                .WithErrors(initResult);
        }

        // The Utility Panel is the one area that holds no document area, so this routes the dock without a
        // second test of what the area is.
        var documentArea = area.GetDocumentArea();
        if (documentArea is null)
        {
            return DockUtilityInPanel(panelView);
        }

        return DockUtilityAsDocument(panelView, documentArea.Value);
    }

    private IReadOnlyList<WorkspaceArea> GetAllowedAreas(EditorId utilityId)
    {
        var railItem = FindUtilityItem(utilityId);
        if (railItem is null)
        {
            return UtilityDescriptor.DefaultAllowedAreas;
        }

        return railItem.AllowedAreas;
    }

    // The rail item holding a utility's declaration. Null only for an id the register does not hold, which
    // cannot happen for a live utility, so the callers above fall back to the manifest defaults.
    private UtilityRailItem? FindUtilityItem(EditorId utilityId)
    {
        return _utilityItems.FirstOrDefault(item => item.ItemId == utilityId);
    }

    // Names a set of areas by their tokens, in the order the areas read on screen, for an error message.
    private static string DescribeAreas(IReadOnlyList<WorkspaceArea> areas)
    {
        var tokens = new List<string>();
        foreach (var area in WorkspaceAreaHelper.AllAreas)
        {
            if (areas.Contains(area))
            {
                tokens.Add($"'{area.ToToken()}'");
            }
        }

        return string.Join(", ", tokens);
    }

    // Docks a utility into a document tab in the area's primary section, reusing its live WebView. A utility
    // already docked in another document area moves; one already in this area is activated in place.
    private Result DockUtilityAsDocument(CustomUtilityView panelView, DocumentArea documentArea)
    {
        var documentsPanel = (WorkspacePanel)DocumentsPanel;
        var area = documentArea.GetWorkspaceArea();

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

        var section = documentArea.GetPrimarySection();
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
        if (panelView.Area == WorkspaceArea.Utility)
        {
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
