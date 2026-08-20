using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.WebHost;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Views;

using IWorkspacePanelLogger = Logging.ILogger<WorkspacePanel>;

public sealed partial class WorkspacePanel : UserControl, IDocumentsPanel
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkspacePanelLogger _logger;
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IWindowModeService _windowModeService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWebViewFocusRegistry _webViewFocusRegistry;
    private readonly IFocusService _focusService;
    private readonly Dictionary<DocumentArea, DocumentToolbar> _areaToolbars = new();

    private bool _isShuttingDown = false;

    public WorkspacePanelViewModel ViewModel { get; }

    // Manages the document sections inside the surface container's area grids.
    private DocumentSectionContainer SectionContainer { get; }

    public IReadOnlyList<DocumentSection> VisibleSections => SectionContainer.Areas.VisibleSections;

    public ResourceKey ActiveDocument
    {
        get => SectionContainer.ActiveDocument;
        set => SectionContainer.SetActiveDocument(value);
    }

    public double MinimumWidth => SurfaceContainer.MinimumSize.Width;

    public double MinimumHeight => SurfaceContainer.MinimumSize.Height;

    public WorkspacePanel(
        IServiceProvider serviceProvider,
        IWorkspacePanelLogger logger,
        IMessengerService messengerService,
        ICommandService commandService,
        IWindowModeService windowModeService,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer)
    {
        InitializeComponent();

        _serviceProvider = serviceProvider;
        _logger = logger;
        _messengerService = messengerService;
        _commandService = commandService;
        _windowModeService = windowModeService;
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;
        _webViewFocusRegistry = serviceProvider.AcquireService<IWebViewFocusRegistry>();
        _focusService = serviceProvider.AcquireService<IFocusService>();

        ViewModel = serviceProvider.AcquireService<WorkspacePanelViewModel>();

        this.DataContext = ViewModel;

        SectionContainer = new DocumentSectionContainer(SurfaceContainer);

        // Wire up section container events
        SectionContainer.ActiveDocumentChanged += OnActiveDocumentChanged;
        SectionContainer.DocumentsLayoutChanged += OnSectionDocumentsLayoutChanged;
        SectionContainer.CloseRequested += OnSectionCloseRequested;
        SectionContainer.ContextMenuActionRequested += OnSectionContextMenuActionRequested;
        SectionContainer.Areas.AreaLayoutChanged += OnAreaLayoutChanged;
        SectionContainer.FilesDropped += OnSectionFilesDropped;

        // Surface sizes are dragged on the surface container's splitters and persisted here.
        SurfaceContainer.SurfaceSizeChanged += OnSurfaceSizeChanged;
        SurfaceContainer.SurfaceSizeResetRequested += OnSurfaceSizeResetRequested;
        SurfaceContainer.StoredSurfaceSizesNeeded += OnStoredSurfaceSizesNeeded;

        SectionContainer.InitializeTabDrag(TabDragOverlay, this);
        ConfigureResourceDropTarget();

        CreateAreaToolbars();

        // This panel composes every workspace surface, so it is the only place both panel references are
        // in hand. Registering here keeps the Utility Panel instance out of the documents interface.
        var workspaceWrapper = serviceProvider.AcquireService<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.SetPanels(SurfaceContainer.UtilityPanel, this);

        Loaded += WorkspacePanel_Loaded;
        Unloaded += WorkspacePanel_Unloaded;
    }

    private void OnActiveDocumentChanged(ResourceKey documentResource, ActiveDocumentChangeReason reason)
    {
        if (_isShuttingDown)
        {
            return;
        }

        // Closing the last document in an isolated area moves the active document to another area, so
        // the isolation follows it rather than leaving an empty panel on screen.
        if (SectionContainer.Areas.IsolatedArea is not null)
        {
            SectionContainer.Areas.SetIsolatedArea(SectionContainer.ActiveSection.GetArea());
        }

        ViewModel.OnActiveDocumentChanged(documentResource);

        // The keyboard follows the active document, so every path that changes it carries focus without
        // having to remember to: opening, closing onto the next tab, moving a tab between sections. A
        // restore is the exception, because the user did not ask for it. An empty resource means the last
        // document closed, and the surface that held focus reports its own teardown.
        if (reason == ActiveDocumentChangeReason.Activated
            && !documentResource.IsEmpty)
        {
            FocusActivatedDocument(documentResource);
        }
    }

    private void OnSectionDocumentsLayoutChanged(DocumentSectionView sectionView, List<ResourceKey> documents)
    {
        if (_isShuttingDown)
        {
            return;
        }

        ViewModel.OnDocumentLayoutChanged();
    }

    private void OnSectionCloseRequested(DocumentSectionView sectionView, ResourceKey fileResource)
    {
        // A docked utility is never destroyed: closing its tab docks it back into the Utility Panel instead.
        ViewModel.OnCloseDocumentRequested(fileResource);
    }

    private void OnSectionContextMenuActionRequested(DocumentSectionView sectionView, DocumentTab tab, DocumentTabMenuAction action)
    {
        OnDocumentTabContextMenuAction(tab, action);
    }

    // Builds the per-area toolbar hosted in each area's tab strip footer. It carries the collapse button, so
    // only the collapsible areas get one; splitting is driven from the document tab context menu.
    private void CreateAreaToolbars()
    {
        foreach (var area in DocumentLayoutHelper.AllAreas)
        {
            if (!area.IsCollapsible())
            {
                continue;
            }

            var toolbar = new DocumentToolbar(area);
            toolbar.CollapseAreaRequested += OnToolbarCollapseAreaRequested;

            _areaToolbars[area] = toolbar;
            SectionContainer.Areas.SetAreaToolbar(area, toolbar);
        }
    }

    private void OnAreaLayoutChanged(DocumentArea area, bool isSplit, double splitRatio)
    {
        if (_isShuttingDown)
        {
            return;
        }

        ViewModel.OnAreaLayoutChanged(area, isSplit, splitRatio);
    }

    private void OnSurfaceSizeChanged(WorkspaceSurface surface, double size)
    {
        if (_isShuttingDown)
        {
            return;
        }

        ViewModel.StoreSurfaceSize(surface, (float)size);
    }

    private void OnSurfaceSizeResetRequested(WorkspaceSurface surface)
    {
        ViewModel.ResetSurfaceSize(surface);
    }

    private void OnToolbarCollapseAreaRequested(DocumentArea area)
    {
        ViewModel.SetAreaVisible(area, false);
    }

    private void OnSectionFilesDropped(DocumentSectionView targetSectionView, List<IResource> resources, int insertionSlot)
    {
        // The built-in drag-and-drop path: the section maps the drop point to an insertion slot. The
        // pointer-driven path arrives through TryDrop instead, which carries the divider's insertion slot.
        _ = HandleDroppedFiles(targetSectionView, resources, insertionSlot);
    }

    // The insertion slot is where the drop landed in the target section's tab order. Each branch below makes
    // the dropped document the active one, which is what carries the keyboard to it.
    private async Task HandleDroppedFiles(DocumentSectionView targetSectionView, List<IResource> resources, int insertionSlot)
    {
        if (_isShuttingDown)
        {
            return;
        }

        var targetSection = targetSectionView.Section;
        int droppedFileOffset = 0;

        foreach (var resource in resources)
        {
            if (resource is not IFileResource fileResource)
            {
                continue;
            }

            var fileResourceKey = ViewModel.GetResourceKey(fileResource);

            // Several files dropped at one slot insert consecutively from it.
            int slot = insertionSlot + droppedFileOffset;
            droppedFileOffset++;

            // Check if the file is already open in any section
            var existingLocation = SectionContainer.FindDocumentTab(fileResourceKey);
            if (existingLocation is not null)
            {
                var existingSectionView = existingLocation.SectionView;
                var existingTab = existingLocation.Tab;

                // Already open - move to the target section, otherwise reorder within it, then select it
                if (existingSectionView.Section != targetSection)
                {
                    SectionContainer.MoveTabToSection(existingTab, targetSection, slot);
                }
                else
                {
                    existingSectionView.ReorderTab(existingTab, slot);
                    existingSectionView.SelectTab(existingTab);
                    SectionContainer.ActivateDocument(fileResourceKey, targetSection);
                }
            }
            else
            {
                // Not open - use the command to open in the target section at the divider slot
                await _commandService.ExecuteAsync<IOpenDocumentCommand>(command =>
                {
                    command.FileResource = fileResourceKey;
                    command.TargetSection = targetSection;
                    command.TargetTabIndex = slot;
                });
            }
        }
    }

    // Gives the active document keyboard focus without the active document changing, which is what normally
    // carries it: a layout-mode change collapses the panels while the keyboard is still on one of them.
    public void FocusActiveDocument()
    {
        var activeDocument = ActiveDocument;
        if (activeDocument.IsEmpty)
        {
            return;
        }

        FocusActivatedDocument(activeDocument);
    }

    // Gives an explicitly activated document keyboard focus: tab clicks, drops, and opens all route here,
    // so keys follow the document the user just acted on rather than staying with the panel the action was
    // issued from. A view whose web surface is still initializing takes focus when it registers, and the
    // trailing focus events of the interaction that activated the document are reconciled away, so neither
    // needs waiting out here.
    private void FocusActivatedDocument(ResourceKey fileResource)
    {
        // Queued below the gesture that asked for the activation. Opening runs as a command, so it can
        // complete while the originating gesture's own focus events are still being dispatched: a
        // double-click in the Explorer finishes claiming the tree after the document has taken focus,
        // and the keys then go to the tree while the document looks focused.
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                var location = SectionContainer.FindDocumentTab(fileResource);
                var documentView = location?.Tab.Content as IDocumentView;
                if (documentView is null)
                {
                    return;
                }

                documentView.FocusDocument();

                // Hold the panel against the rest of the gesture's focus events, which can still arrive
                // after the document has taken focus.
                FocusIntent.SuppressPanelClaimsUntilNextInput();
            });
    }

    private void WorkspacePanel_Loaded(object sender, RoutedEventArgs e)
    {
        // Listen for layout mode changes to show/hide the tab strip in Presentation mode
        _messengerService.Register<LayoutModeChangedMessage>(this, OnLayoutModeChanged);

        // The collapsible areas follow the workspace surface visibility.
        _messengerService.Register<SurfaceVisibilityChangedMessage>(this, OnSurfaceVisibilityChanged);

        // The Bottom area's alignment decides which surfaces it spans across.
        _messengerService.Register<BottomAreaAlignmentChangedMessage>(this, OnBottomAreaAlignmentChanged);

        // Surface sizes are restored and reset through the workspace settings facade.
        ViewModel.SurfaceSizeChanged += OnStoredSurfaceSizeChanged;
        SectionContainer.Areas.SetBottomAreaAlignment(ViewModel.BottomAreaAlignment);
        ApplyStoredSurfaceSizes();
        ApplyAreaVisibility();

        // Listen for document view focus to update active document
        _messengerService.Register<DocumentViewFocusedMessage>(this, OnDocumentViewFocused);

        // Listen for layout reset requests to reset section count
        _messengerService.Register<ResetLayoutRequestedMessage>(this, OnResetLayoutRequested);

        // Listen for the close document keyboard shortcuts
        _messengerService.Register<CloseActiveDocumentRequestedMessage>(this, OnCloseActiveDocumentRequested);
        _messengerService.Register<CloseAllDocumentsRequestedMessage>(this, OnCloseAllDocumentsRequested);

        // Listen for requests to flash a document tab (e.g. when a utility is surfaced or a document reopened)
        _messengerService.Register<FlashDocumentMessage>(this, OnFlashDocumentRequested);

        // Register how this panel takes keyboard focus, so the focus service can hand it back after an
        // interaction moves it away transiently. Without it a modal dialog raised over a document leaves the
        // keyboard nowhere when it closes.
        _focusService.SetPanelFocusHandler(WorkspacePanelId.Documents, FocusActiveDocument);

        // Apply the current layout mode. It survives a project switch, so a workspace can load straight
        // into Focus or Presentation.
        ApplyIsolatedArea(_windowModeService.LayoutMode);
        UpdateTabStripVisibility(_windowModeService.LayoutMode);

        RegisterAsResourceDropTarget();
    }

    private void OnCloseActiveDocumentRequested(object recipient, CloseActiveDocumentRequestedMessage message)
    {
        var activeTab = GetFocusedActiveDocumentTab();
        if (activeTab is null)
        {
            return;
        }

        CloseTab(activeTab);
    }

    private void OnCloseAllDocumentsRequested(object recipient, CloseAllDocumentsRequestedMessage message)
    {
        var activeTab = GetFocusedActiveDocumentTab();
        if (activeTab is null)
        {
            return;
        }

        CloseAllTabs(activeTab);
    }

    // Resolves the active document's tab, but only while the documents panel holds focus. The close shortcuts
    // must not close a hidden document when the user is working in the console or another panel.
    private DocumentTab? GetFocusedActiveDocumentTab()
    {
        if (_isShuttingDown)
        {
            return null;
        }

        if (_focusService.FocusedPanel != WorkspacePanelId.Documents)
        {
            return null;
        }

        var activeResource = SectionContainer.ActiveDocument;
        if (activeResource.IsEmpty)
        {
            return null;
        }

        var location = SectionContainer.FindDocumentTab(activeResource);

        return location?.Tab;
    }

    private void OnDocumentViewFocused(object recipient, DocumentViewFocusedMessage message)
    {
        if (_isShuttingDown)
        {
            return;
        }

        // Find the section containing this document and update the active document
        var location = SectionContainer.FindDocumentTab(message.DocumentResource);
        if (location is not null)
        {
            SectionContainer.ActivateDocument(message.DocumentResource, location.SectionView.Section);
        }
    }

    private void OnResetLayoutRequested(object recipient, ResetLayoutRequestedMessage message)
    {
        if (_isShuttingDown)
        {
            return;
        }

        // Fold every area back to a single section at its default split position
        _ = SectionContainer.Areas.ResetAreaLayoutAsync();
    }

    private void WorkspacePanel_Unloaded(object sender, RoutedEventArgs e)
    {
        UnregisterAsResourceDropTarget();
        ViewModel.SurfaceSizeChanged -= OnStoredSurfaceSizeChanged;
        ViewModel.OnViewUnloaded();
        _focusService.SetPanelFocusHandler(WorkspacePanelId.Documents, null);
        _messengerService.UnregisterAll(this);
    }

    private void OnLayoutModeChanged(object recipient, LayoutModeChangedMessage message)
    {
        ApplyIsolatedArea(message.LayoutMode);
        UpdateTabStripVisibility(message.LayoutMode);

        // Entering a mode that hides the side panels can leave keyboard focus on a now-hidden panel
        // (e.g. the Explorer), which stops app shortcuts like F11 from being delivered until the user
        // clicks back into the content. Move focus to the active document's editor so the shortcuts
        // keep working. The Default layout keeps the panels, so its focus is unaffected.
        if (message.LayoutMode == LayoutMode.Focus ||
            message.LayoutMode == LayoutMode.Presentation)
        {
            FocusActiveDocument();
        }
    }

    // Focus and Presentation give the active document's area the whole panel and hide the other two. The
    // area keeps its own split, so a split area still shows both documents. Leaving them restores the
    // areas the user had.
    private void ApplyIsolatedArea(LayoutMode layoutMode)
    {
        bool isolateActiveArea = layoutMode == LayoutMode.Focus ||
            layoutMode == LayoutMode.Presentation;

        if (isolateActiveArea)
        {
            SectionContainer.Areas.SetIsolatedArea(SectionContainer.ActiveSection.GetArea());
            return;
        }

        SectionContainer.Areas.SetIsolatedArea(null);
        ApplyStoredSurfaceSizes();
    }

    private void UpdateTabStripVisibility(LayoutMode layoutMode)
    {
        // In Presentation mode, hide the tab strip to show only the document content.
        bool showTabStrip = layoutMode != LayoutMode.Presentation;
        SectionContainer.UpdateTabStripVisibility(showTabStrip);

        // The toolbar carries the collapse button, which only means something while the area sits beside
        // the others. Focus and Presentation give it the whole panel, so there is no edge left to collapse
        // into and the button is hidden rather than left offering an action it cannot perform.
        var toolbarVisibility = layoutMode == LayoutMode.Default
            ? Visibility.Visible
            : Visibility.Collapsed;

        foreach (var toolbar in _areaToolbars.Values)
        {
            toolbar.Visibility = toolbarVisibility;
        }
    }

    private void OnSurfaceVisibilityChanged(object recipient, SurfaceVisibilityChangedMessage message)
    {
        if (_isShuttingDown)
        {
            return;
        }

        ApplyAreaVisibility();
    }

    // Shows or hides the collapsible areas to match the workspace surface visibility. Hiding an area
    // leaves its tabs in place, so the documents reappear where they were when it is shown again.
    private void ApplyAreaVisibility()
    {
        SectionContainer.Areas.SetAreaVisible(DocumentArea.Bottom, ViewModel.IsAreaVisible(DocumentArea.Bottom));
        SectionContainer.Areas.SetAreaVisible(DocumentArea.Side, ViewModel.IsAreaVisible(DocumentArea.Side));

        // The areas draw a left edge only while the Utility Panel is there to face. Hiding it, whether from
        // the toolbar or by entering Focus, leaves that edge on the application border instead.
        SectionContainer.Areas.SetUtilityPanelPresented(ViewModel.IsUtilityPanelVisible);

        ApplyStoredSurfaceSizes();
    }

    private void ApplyStoredSurfaceSizes()
    {
        SurfaceContainer.SetSurfaceSize(WorkspaceSurface.UtilityPanel, ViewModel.GetSurfaceSize(WorkspaceSurface.UtilityPanel));
        SurfaceContainer.SetSurfaceSize(WorkspaceSurface.BottomArea, ViewModel.GetSurfaceSize(WorkspaceSurface.BottomArea));
        SurfaceContainer.SetSurfaceSize(WorkspaceSurface.SideArea, ViewModel.GetSurfaceSize(WorkspaceSurface.SideArea));
    }

    private void OnStoredSurfaceSizeChanged(WorkspaceSurface surface)
    {
        SurfaceContainer.SetSurfaceSize(surface, ViewModel.GetSurfaceSize(surface));
    }

    // The stored sizes are re-applied rather than the current ones held down, so a surface narrowed to fit a
    // smaller window returns to the size the user set once the window gives the space back.
    private void OnStoredSurfaceSizesNeeded()
    {
        if (_isShuttingDown)
        {
            return;
        }

        ApplyStoredSurfaceSizes();
    }

    private void OnBottomAreaAlignmentChanged(object recipient, BottomAreaAlignmentChangedMessage message)
    {
        if (_isShuttingDown)
        {
            return;
        }

        SectionContainer.Areas.SetBottomAreaAlignment(message.Alignment);
    }

    // Mounts the section a document is about to open into. Naming an unsplit area's secondary section
    // splits it, so the request can be satisfied where it asked for. A section in a collapsed area is left
    // alone: the area keeps its tabs while hidden.
    private DocumentSection EnsureSectionMounted(DocumentSection section)
    {
        var area = section.GetArea();
        if (section.IsSecondarySection()
            && !SectionContainer.Areas.IsAreaSplit(area))
        {
            SectionContainer.Areas.SetAreaSplit(area, true);
        }

        return section;
    }

    public bool IsAreaSplit(DocumentArea area)
    {
        return SectionContainer.Areas.IsAreaSplit(area);
    }

    public void SetAreaSplit(DocumentArea area, bool isSplit)
    {
        SectionContainer.Areas.SetAreaSplit(area, isSplit);
    }

    public void ReconcileAreaSplit(DocumentArea area)
    {
        SectionContainer.Areas.ReconcileAreaSplit(area);
    }

    public double GetAreaSplitRatio(DocumentArea area)
    {
        return SectionContainer.Areas.GetAreaSplitRatio(area);
    }

    public void SetAreaSplitRatio(DocumentArea area, double ratio)
    {
        SectionContainer.Areas.SetAreaSplitRatio(area, ratio);
    }

    public async Task ResetAreaLayoutAsync()
    {
        await SectionContainer.Areas.ResetAreaLayoutAsync();
    }

    public IReadOnlyList<OpenDocumentInfo> GetOpenDocuments()
    {
        var documents = new List<OpenDocumentInfo>();

        // Includes sections in a collapsed area, which keep their documents open.
        foreach (var sectionView in SectionContainer.GetAllSections())
        {
            int tabOrder = 0;
            foreach (var tab in sectionView.GetAllTabs())
            {
                var address = new DocumentAddress(
                    WindowIndex: 0,
                    Section: sectionView.Section,
                    TabOrder: tabOrder++);

                documents.Add(new OpenDocumentInfo(
                    tab.ViewModel.FileResource,
                    address,
                    tab.ViewModel.EditorId));
            }
        }

        return documents;
    }

    public async Task<Result<OpenDocumentOutcome>> OpenDocument(ResourceKey fileResource, OpenDocumentOptions? options = null)
    {
        var effectiveOptions = options ?? new OpenDocumentOptions();

        // Resolve the target section from the address, defaulting to the section unaddressed opens land
        // in. An address naming a section whose area is not split folds into that area's primary section.
        var address = effectiveOptions.Address;

        DocumentSection section;
        if (address is not null)
        {
            section = EnsureSectionMounted(address.Section);
        }
        else
        {
            section = DocumentLayoutHelper.DefaultOpenSection;
        }

        // Check if the file is already opened in any section
        var existingLocation = SectionContainer.FindDocumentTab(fileResource);
        if (existingLocation is not null)
        {
            var existingSectionView = existingLocation.SectionView;
            var existingTab = existingLocation.Tab;

            // Honor an explicit editor request even when the existing tab's EditorId is Empty.
            bool isDifferentEditor = !effectiveOptions.EditorId.IsEmpty &&
                effectiveOptions.EditorId != existingTab.ViewModel.EditorId;

            if (isDifferentEditor)
            {
                var closeResult = await existingTab.ViewModel.CloseDocument(forceClose: false);
                if (closeResult.IsFailure)
                {
                    return Result<OpenDocumentOutcome>.Fail($"Failed to close existing document before reopening with a different editor: '{fileResource}'")
                        .WithErrors(closeResult);
                }

                if (closeResult.Value == CloseDocumentOutcome.Cancelled)
                {
                    // The existing tab refused to close: either the user declined a save-prompt dialog,
                    // or the document view itself vetoed via CanClose.
                    return Result<OpenDocumentOutcome>.Ok(OpenDocumentOutcome.Cancelled);
                }

                existingSectionView.RemoveTab(existingTab);
                NotifyLayoutChanged();

                // Without an explicit address the document reopens in the section it was already in.
                var reopenSection = address is not null ? section : existingSectionView.Section;
                var tabOrder = effectiveOptions.Address?.TabOrder ?? 0;
                var reopenAddress = new DocumentAddress(WindowIndex: 0, Section: reopenSection, TabOrder: tabOrder);
                var reopenOptions = effectiveOptions with { Address = reopenAddress };
                return await OpenDocument(fileResource, reopenOptions);
            }

            // Without an explicit address the existing tab stays in its own
            // section. Pulling it into the Main area would yank it from under
            // the user.
            if (address is null)
            {
                section = existingSectionView.Section;
            }

            // If a different section was explicitly requested, move it there.
            if (existingSectionView.Section != section)
            {
                SectionContainer.MoveTabToSection(existingTab, section);
            }

            if (effectiveOptions.Activate)
            {
                var targetSection = SectionContainer.GetSection(section);
                targetSection.SelectTab(existingTab);
                SectionContainer.ActivateDocument(fileResource, section);
            }

            if (effectiveOptions.ForceReload)
            {
                var reloadResult = await existingTab.ViewModel.ReloadDocument();
                if (reloadResult.IsFailure)
                {
                    return Result<OpenDocumentOutcome>.Fail($"Failed to reload document: {fileResource}")
                        .WithErrors(reloadResult);
                }
            }

            if (!string.IsNullOrEmpty(effectiveOptions.Location))
            {
                await NavigateToLocation(fileResource, effectiveOptions.Location);
            }

            return Result<OpenDocumentOutcome>.Ok(OpenDocumentOutcome.Opened);
        }

        // Resolve the file path from the resource key
        var resolveResult = ViewModel.ResolveResourcePath(fileResource);
        if (resolveResult.IsFailure)
        {
            return Result<OpenDocumentOutcome>.Fail($"Failed to resolve path for resource: '{fileResource}'")
                .WithErrors(resolveResult);
        }
        var filePath = resolveResult.Value;

        // Open in the specified section
        var targetSectionForNew = SectionContainer.GetSection(section);

        var documentTab = new DocumentTab();
        documentTab.ViewModel.FileResource = fileResource;
        documentTab.ViewModel.FilePath = filePath;

        // When the caller names the editor (a utility launcher always does), stamp the utility's manifest title
        // and icon before the tab enters the visual tree, so it never briefly flashes the raw backing-file name
        // while the view is created. Paths that only learn the editor id from the created view re-apply below.
        ApplyUtilityTabMetadata(documentTab, effectiveOptions.EditorId);
        if (!documentTab.ViewModel.IsUtility)
        {
            documentTab.ViewModel.DocumentName = fileResource.ResourceName;
        }

        if (address is not null)
        {
            targetSectionForNew.InsertTab(documentTab, address.TabOrder);
        }
        else
        {
            targetSectionForNew.AddTab(documentTab);
        }

        if (effectiveOptions.Activate)
        {
            targetSectionForNew.SelectTab(documentTab);
        }

        var createResult = await ViewModel.CreateDocumentView(fileResource, effectiveOptions.EditorId);
        if (createResult.IsFailure)
        {
            RemoveTabFromSection(targetSectionForNew, documentTab);
            return Result<OpenDocumentOutcome>.Fail($"Failed to create document view for file resource: '{fileResource}'")
                .WithErrors(createResult);
        }
        var documentView = createResult.Value;

        documentTab.ViewModel.DocumentView = documentView;
        documentTab.Content = documentView;

        UpdateEditorDisplayName(documentTab, documentView.EditorId);

        // Apply the manifest title and icon for paths that only learn the editor id from the created view.
        // The launcher path already stamped it above, and re-applying is idempotent. Runs before
        // UpdateAllTabDisplayNames so the utility title is not overwritten by filename disambiguation.
        ApplyUtilityTabMetadata(documentTab, documentView.EditorId);

        targetSectionForNew.RefreshSelectedTab();
        UpdateAllTabDisplayNames();

        // Announce after the view exists so listeners can act on a fully opened document, whether or not
        // its tab is the active one. The view model owns both halves of the open/close pair.
        documentTab.ViewModel.NotifyDocumentOpened();

        if (effectiveOptions.Activate)
        {
            SectionContainer.ActivateDocument(fileResource, section);
        }

        if (!string.IsNullOrEmpty(effectiveOptions.Location))
        {
            await NavigateToLocation(fileResource, effectiveOptions.Location);
        }

        if (!string.IsNullOrEmpty(effectiveOptions.EditorStateJson))
        {
            await documentView.RestoreEditorStateAsync(effectiveOptions.EditorStateJson);
        }

        return Result<OpenDocumentOutcome>.Ok(OpenDocumentOutcome.Opened);
    }

    public async Task<Result> NavigateToLocation(ResourceKey fileResource, string location)
    {
        var documentLocation = SectionContainer.FindDocumentTab(fileResource);
        if (documentLocation is not null)
        {
            var documentView = documentLocation.Tab.Content as IDocumentView;
            if (documentView != null)
            {
                return await documentView.NavigateToLocation(location);
            }
            return Result.Ok();
        }

        return Result.Fail($"No opened document found for file resource: '{fileResource}'");
    }

    public async Task<Result> CloseDocument(ResourceKey fileResource, bool forceClose)
    {
        var location = SectionContainer.FindDocumentTab(fileResource);
        if (location is not null)
        {
            var sectionView = location.SectionView;
            var documentTab = location.Tab;

            // Capture editor state before the document view is torn down by CloseDocument.
            // If the close is cancelled this value is discarded.
            var capturedEditorState = await TryCaptureEditorStateAsync(documentTab);

            var closeResult = await documentTab.ViewModel.CloseDocument(forceClose);
            if (closeResult.IsFailure)
            {
                return Result.Fail($"An error occurred when closing the document for file resource: '{fileResource}'")
                    .WithErrors(closeResult);
            }

            if (closeResult.Value == CloseDocumentOutcome.Closed)
            {
                // Persist the captured state so the next open can restore it. The service call
                // is best-effort and handles its own errors.
                await ViewModel.StoreDocumentEditorState(fileResource, capturedEditorState);

                // Get the tab index before removing it (needed for selecting next document)
                int tabIndex = sectionView.GetTabIndex(documentTab);

                // Handle selection of next document before removing the tab
                SectionContainer.HandleDocumentClosing(fileResource, sectionView.Section, tabIndex);

                RemoveTabFromSection(sectionView, documentTab);

                // Update all tab names since closing a tab may resolve filename ambiguity
                UpdateAllTabDisplayNames();
            }

            return Result.Ok();
        }

        // We failed to find any open document for this fileResource, but this is the
        // state we were trying to get into anyway, so we consider this a success.

        return Result.Ok();
    }

    private void OnFlashDocumentRequested(object recipient, FlashDocumentMessage message)
    {
        // Flashing is a transient view effect, so it is deferred until the tab that prompted it (a freshly
        // docked, activated, or opened tab) has settled into the visual tree.
        var fileResource = message.FileResource;
        _ = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => FlashDocument(fileResource));
    }

    /// <summary>
    /// Briefly flashes the open document tab for the given resource to draw attention to it. A no-op when the
    /// document is not open.
    /// </summary>
    private void FlashDocument(ResourceKey fileResource)
    {
        var location = SectionContainer.FindDocumentTab(fileResource);
        location?.Tab.FlashAttention();
    }

    // Removes a tab, folding its area back when that leaves one of a split area's sections empty.
    private void RemoveTabFromSection(DocumentSectionView sectionView, DocumentTab documentTab)
    {
        var area = sectionView.Section.GetArea();

        sectionView.RemoveTab(documentTab);
        SectionContainer.Areas.ReconcileAreaSplit(area);
    }

    /// <summary>
    /// Returns the editor state JSON for the given tab, or null if the view isn't ready, hasn't
    /// been created, or anything throws. Best-effort: editor state is a user convenience, not data.
    /// </summary>
    private static async Task<string?> TryCaptureEditorStateAsync(DocumentTab documentTab)
    {
        var documentView = documentTab.ViewModel.DocumentView;
        if (documentView is null)
        {
            return null;
        }

        try
        {
            return await documentView.TrySaveEditorStateAsync();
        }
        catch
        {
            return null;
        }
    }

    public async Task<Result> SaveModifiedDocuments(double deltaTime)
    {
        int savedCount = 0;
        int pendingSaveCount = 0;
        List<ResourceKey> failedSaves = new();
        bool updateResourcesRequired = false;

        foreach (var sectionView in SectionContainer.GetAllSections())
        {
            foreach (var documentTab in sectionView.GetAllTabs())
            {
                var documentView = documentTab.Content as IDocumentView;
                Guard.IsNotNull(documentView);

                if (documentView.HasUnsavedChanges)
                {
                    var updateResult = documentView.UpdateSaveTimer(deltaTime);
                    Guard.IsTrue(updateResult.IsSuccess); // Should never fail

                    var shouldSave = updateResult.Value;
                    if (!shouldSave)
                    {
                        pendingSaveCount++;
                        continue;
                    }

                    var saveResult = await documentView.SaveDocument();
                    if (saveResult.IsFailure)
                    {
                        // A save failure against a document whose cached state is
                        // not Writable is the expected outcome of the read-only
                        // gate in LocalResourceFileSystem. Log it for diagnostics
                        // but do not surface an alert, otherwise every auto-save
                        // tick on a locked file with buffered changes would spam
                        // the user.
                        if (documentView.WritableState == WritableState.Writable)
                        {
                            failedSaves.Add(documentTab.ViewModel.FileResource);

                            // A failed save against a cache that still reads Writable
                            // suggests an external attribute flip slipped past the
                            // watcher. Schedule a resource update so the cache catches up.
                            updateResourcesRequired = true;
                        }
                        else
                        {
                            _logger.LogDebug($"Skipped save for non-writable document: '{documentTab.ViewModel.FileResource}'");
                        }
                    }
                    else
                    {
                        savedCount++;
                    }
                }
            }
        }

        if (updateResourcesRequired)
        {
            // Debounced inside the resource service so a burst of failures from
            // many open files collapses into one project-tree rebuild.
            _commandService.Execute<IUpdateResourcesCommand>();
        }

        if (failedSaves.Count > 0)
        {
            // Log the error with all failed files
            var errorMessage = $"Failed to save the following documents: {string.Join(", ", failedSaves)}";
            _logger.LogError(errorMessage);

            // Show localized alert to the user with just the first file name
            // Multiple simultaneous failures are extremely unlikely
            var firstFailedFile = failedSaves[0].ToString();
            var alertTitle = _stringLocalizer.GetString("Documents_SaveDocumentFailedTitle");
            var alertMessage = _stringLocalizer.GetString("Documents_SaveDocumentFailedGeneric", firstFailedFile);

            // Fire-and-forget to avoid blocking the save loop
            _ = _dialogService.ShowAlertDialogAsync(alertTitle, alertMessage);

            return Result.Fail(errorMessage);
        }

        if (savedCount > 0)
        {
            _logger.LogDebug($"Saved {savedCount} modified documents");
        }

        ViewModel.UpdatePendingSaveCount(pendingSaveCount);

        return Result.Ok();
    }

    public Result ActivateDocument(ResourceKey fileResource)
    {
        var location = SectionContainer.FindDocumentTab(fileResource);
        if (location is null)
        {
            return Result.Fail($"No opened document found for file resource: '{fileResource}'");
        }

        // Section.SelectTab alone does not update the container's active-section
        // / active-document tracking, so the new tab would be selected within
        // its section but not surfaced as the workspace's active document.
        SectionContainer.ActivateDocument(fileResource, location.SectionView.Section);

        return Result.Ok();
    }

    public IDocumentView? GetDocumentView(ResourceKey fileResource)
    {
        var location = SectionContainer.FindDocumentTab(fileResource);
        return location?.Tab.Content as IDocumentView;
    }

    public async Task<Result> ChangeDocumentResource(ResourceKey oldResource, DocumentViewType oldDocumentType, ResourceKey newResource, string newResourcePath, DocumentViewType newDocumentType)
    {
        // Find the document tab for the old resource
        var location = SectionContainer.FindDocumentTab(oldResource);

        if (location is null)
        {
            // The document isn't open, so we don't need to do anything
            return Result.Ok();
        }

        var sectionView = location.SectionView;
        var documentTab = location.Tab;

        var oldDocumentView = documentTab.Content as IDocumentView;
        Guard.IsNotNull(oldDocumentView);

        if (oldDocumentType == newDocumentType)
        {
            var setResult = await oldDocumentView.SetFileResource(newResource);
            if (setResult.IsFailure)
            {
                return Result.Fail($"Failed to set file resource for document: '{newResource}'")
                    .WithErrors(setResult);
            }

            // Reload the content so the document reflects the current file state
            // after the resource move or rename.
            var loadResult = await oldDocumentView.LoadContent();
            if (loadResult.IsFailure)
            {
                return Result.Fail($"Failed to reload content for document: '{newResource}'")
                    .WithErrors(loadResult);
            }
        }
        else
        {
            var createResult = await ViewModel.CreateDocumentView(newResource);
            if (createResult.IsFailure)
            {
                return Result.Fail($"Failed to create document view for resource: '{newResource}'")
                    .WithErrors(createResult);
            }
            var newDocumentView = createResult.Value;

            // Clean up the old DocumentView state
            await oldDocumentView.PrepareToClose();

            // Resource (and possibly extension) changed. Refresh content and label.
            documentTab.ViewModel.DocumentView = newDocumentView;
            documentTab.Content = newDocumentView;
            UpdateEditorDisplayName(documentTab, newDocumentView.EditorId);

            // At this point there should be no remaining references to oldDocumentView, so it should go
            // out of scope and eventually be cleaned up by GC.

            // Check if this document is the selected tab and force refresh if so
            if (sectionView.GetSelectedDocument() == oldResource)
            {
                sectionView.RefreshSelectedTab();
            }
        }

        documentTab.ViewModel.FileResource = newResource;
        documentTab.ViewModel.DocumentName = newResource.ResourceName;
        documentTab.ViewModel.FilePath = newResourcePath;

        // Update all tab names to handle any filename ambiguity changes
        UpdateAllTabDisplayNames();

        return Result.Ok();
    }

    // Sets the tab's recorded editor id and display label.
    private void UpdateEditorDisplayName(DocumentTab documentTab, EditorId editorId)
    {
        var displayInfo = ViewModel.ResolveEditorDisplayInfo(documentTab.ViewModel.FileResource, editorId);
        if (displayInfo is not null)
        {
            documentTab.ViewModel.EditorId = displayInfo.EditorId;
            documentTab.ViewModel.EditorDisplayName = displayInfo.EditorDisplayName;
        }
    }

    private void UpdateAllTabDisplayNames()
    {
        // Collect all tabs from all sections. Utility tabs keep their manifest title, so they are
        // excluded from filename-based disambiguation.
        var allTabs = new List<DocumentTab>();
        foreach (var sectionView in SectionContainer.GetAllSections())
        {
            allTabs.AddRange(sectionView.GetAllTabs().Where(tab => !tab.ViewModel.IsUtility));
        }

        // Group tabs by their filename
        var tabsByFilename = new Dictionary<string, List<DocumentTab>>();
        foreach (var tab in allTabs)
        {
            var filename = Path.GetFileName(tab.ViewModel.FilePath);
            if (!tabsByFilename.TryGetValue(filename, out var tabList))
            {
                tabList = new List<DocumentTab>();
                tabsByFilename[filename] = tabList;
            }
            tabList.Add(tab);
        }

        // Process each group
        foreach (var group in tabsByFilename)
        {
            var tabs = group.Value;

            if (tabs.Count == 1)
            {
                // Only one tab with this filename - use simple filename
                tabs[0].ViewModel.DocumentName = tabs[0].ViewModel.FileResource.ResourceName;
            }
            else
            {
                // Multiple tabs with same filename - disambiguate using paths
                var tabsToDisambiguate = new Dictionary<DocumentTab, string>();
                foreach (var tab in tabs)
                {
                    tabsToDisambiguate[tab] = tab.ViewModel.FilePath;
                }

                var disambiguatedNames = PathDisambiguationHelper.DisambiguatePaths(tabsToDisambiguate);
                foreach (var entry in disambiguatedNames)
                {
                    entry.Key.ViewModel.DocumentName = entry.Value;
                }
            }
        }
    }

    public void Shutdown()
    {
        // Set shutdown flag to prevent event handlers from triggering workspace updates
        _isShuttingDown = true;

        SectionContainer.Shutdown();
    }

    private void NotifyLayoutChanged()
    {
        ViewModel.OnDocumentLayoutChanged();
    }

}
