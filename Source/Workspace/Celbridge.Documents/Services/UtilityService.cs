using Celbridge.Commands;
using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.Community;
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

    // Spotlight landmark ids for the document shortcut rail buttons. The Utility Panel sets each one as
    // the button's AutomationId, which is what a landmark has to match.
    private const string ProjectSettingsLandmarkId = "project-settings-utility-button";
    private const string CommunityLandmarkId = "community-utility-button";

    // The id scope the project's own document shortcuts are addressed under, matching the built-in ones.
    private const string ProjectShortcutScope = "celbridge";

    // The rail register, in rail order. The built-in utilities are published by the Utility Panel because
    // their descriptors wrap live views. The rest are built here. The two middle lists are both what the
    // project brings, so they share a rail band and the built-in shortcuts hold the last band on their own.
    private readonly List<UtilityRailItem> _builtInUtilityItems = new();
    private readonly List<UtilityRailItem> _contributedItems = new();
    private readonly List<UtilityRailItem> _projectShortcutItems = new();
    private readonly List<UtilityRailItem> _builtInShortcutItems = new();

    // The four lists above as one ordered list, and the same items by id. Rebuilt whenever a list
    // changes, so a reader never pays to assemble them.
    private readonly List<UtilityRailItem> _railItems = new();
    private readonly Dictionary<EditorId, UtilityRailItem> _railItemsById = new();

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

        RebuildRailRegister();
    }

    public IReadOnlyList<UtilityRailItem> GetRailItems()
    {
        return _railItems;
    }

    private void RebuildRailRegister()
    {
        _railItems.Clear();
        _railItems.AddRange(_builtInUtilityItems);
        _railItems.AddRange(_contributedItems);
        _railItems.AddRange(_projectShortcutItems);
        _railItems.AddRange(_builtInShortcutItems);

        _railItemsById.Clear();
        foreach (var railItem in _railItems)
        {
            _railItemsById[railItem.ItemId] = railItem;
        }
    }

    public WorkspaceArea? GetCurrentArea(EditorId itemId)
    {
        // A utility carries its own area because it moves.
        var utility = _utilities.FirstOrDefault(candidate => candidate.UtilityId == itemId);
        if (utility is not null)
        {
            return utility.Area;
        }

        if (!_railItemsById.TryGetValue(itemId, out var railItem))
        {
            return null;
        }

        switch (railItem.Kind)
        {
            case RailItemKind.PanelUtility:
            case RailItemKind.DockableUtility:
                // Reaching here means the panel owns the item's view rather than this service, so the
                // panel is where it is.
                return WorkspaceArea.Utility;

            case RailItemKind.DocumentShortcut:
                // A shortcut's document sits wherever the user last moved its tab. Closed, it occupies no
                // area at all, and DockArea is what says where it would open.
                return FindOpenDocumentArea(railItem.FileResource);

            default:
                throw new NotSupportedException($"Unhandled rail item kind '{railItem.Kind}'");
        }
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

            var railItem = UtilityRailItem.CreateContributedUtility(
                utilityId, landmarkId, descriptor.Icon, displayName, tooltip,
                resource, resolvedEditor.EditorId, railPanelView, descriptor.DockArea);

            _contributedItems.Add(railItem);
        }

        _projectShortcutItems.Clear();
        _projectShortcutItems.AddRange(BuildProjectShortcutItems());

        _builtInShortcutItems.Clear();
        _builtInShortcutItems.AddRange(BuildBuiltInShortcutItems());

        RebuildRailRegister();
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

    // The document shortcuts the project config declares, in the order it lists them. An entry naming
    // something that is not a resource key contributes no button.
    private List<UtilityRailItem> BuildProjectShortcutItems()
    {
        var projectService = _serviceProvider.GetRequiredService<IProjectService>();
        var stringLocalizer = _serviceProvider.GetRequiredService<IStringLocalizer>();
        var iconService = _serviceProvider.GetRequiredService<IIconService>();

        var shortcutItems = new List<UtilityRailItem>();

        var config = projectService.CurrentProject?.Config;
        if (config is null)
        {
            return shortcutItems;
        }

        var documentShortcuts = config.DocumentShortcuts;
        for (int i = 0; i < documentShortcuts.Count; i++)
        {
            var documentShortcut = documentShortcuts[i];

            if (!ResourceKey.TryCreate(documentShortcut.Resource, out var fileResource))
            {
                _logger.LogWarning(
                    $"Document shortcut #{i + 1} names an invalid resource: '{documentShortcut.Resource}'");
                continue;
            }

            // Identified by position rather than by resource: two shortcuts may open the same file, and a
            // rename must not merge them or collide their landmark ids.
            var shortcutNumber = i + 1;
            var itemId = EditorId.Create(ProjectShortcutScope, $"shortcut-{shortcutNumber}");
            var landmarkId = $"document-shortcut-{shortcutNumber}-utility-button";

            var displayName = fileResource.ResourceName;
            var tooltip = stringLocalizer.GetString("UtilityPanel_DocumentShortcutTooltip", displayName);
            var iconName = DocumentShortcutIcon.Resolve(iconService, documentShortcut.Icon);

            // The editor is left unnamed so the shortcut opens the file in whichever editor its extension
            // resolves to, including any the project associates with it.
            var shortcutItem = UtilityRailItem.CreateDocumentShortcut(
                RailItemGroup.ProjectItem,
                itemId,
                landmarkId,
                iconName,
                displayName,
                tooltip,
                fileResource,
                EditorId.Empty,
                documentShortcut.Area);

            shortcutItems.Add(shortcutItem);
        }

        return shortcutItems;
    }

    // The built-in document shortcuts: rail items that open a document and never occupy the panel, so they
    // carry no panel view. A contribution declaring no utility area builds the same shape from its manifest.
    private List<UtilityRailItem> BuildBuiltInShortcutItems()
    {
        var projectService = _serviceProvider.GetRequiredService<IProjectService>();
        var communityService = _serviceProvider.GetRequiredService<ICommunityService>();
        var stringLocalizer = _serviceProvider.GetRequiredService<IStringLocalizer>();
        var iconService = _serviceProvider.GetRequiredService<IIconService>();

        var shortcutItems = new List<UtilityRailItem>();

        // The project file sits at the project root, so its resource key is just the file name. The editor is
        // named so the choice does not depend on extension resolution.
        var project = projectService.CurrentProject;
        if (project is not null)
        {
            var projectFileName = Path.GetFileName(project.ProjectFilePath);
            if (ResourceKey.TryCreate(projectFileName, out var projectFileResource))
            {
                string projectSettingsName = stringLocalizer.GetString("UtilityPanel_ProjectSettingsTooltip");

                var projectSettingsItem = UtilityRailItem.CreateDocumentShortcut(
                    RailItemGroup.BuiltInShortcut,
                    BuiltInShortcutIds.ProjectSettings,
                    ProjectSettingsLandmarkId,
                    iconService.GetIconName(IconSymbol.Sliders),
                    projectSettingsName,
                    projectSettingsName,
                    projectFileResource,
                    BuiltInEditors.ProjectSettingsEditorId,
                    WorkspaceArea.Main);

                shortcutItems.Add(projectSettingsItem);
            }
        }

        string communityName = stringLocalizer.GetString("UtilityPanel_CommunityTooltip");

        var communityItem = UtilityRailItem.CreateDocumentShortcut(
            RailItemGroup.BuiltInShortcut,
            BuiltInShortcutIds.Community,
            CommunityLandmarkId,
            iconService.GetIconName(IconSymbol.People),
            communityName,
            communityName,
            communityService.DocumentResource,
            BuiltInEditors.WebViewEditorId,
            WorkspaceArea.Main);

        shortcutItems.Add(communityItem);

        return shortcutItems;
    }

    public async Task<Result> RestoreDockedUtilityAsync(ResourceKey resource, DocumentAddress address)
    {
        await Task.CompletedTask;

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

        // A live utility is always in the register, so a missing entry cannot happen here.
        if (_railItemsById.TryGetValue(utilityId, out var railItem)
            && railItem.Kind == RailItemKind.PanelUtility)
        {
            return Result.Fail(
                $"Cannot dock utility '{utilityId}' in the '{area.ToToken()}' area: " +
                $"it stays in the Utility Panel.");
        }

        return DockUtilityAsDocument(panelView, dockArea.Value);
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

    public IReadOnlyList<ISaveableWorkspaceItem> GetSaveableItems()
    {
        return new List<ISaveableWorkspaceItem>(_utilities);
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
