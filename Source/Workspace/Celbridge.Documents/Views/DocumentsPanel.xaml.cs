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

using IDocumentsLogger = Logging.ILogger<DocumentsPanel>;

/// <summary>
/// Where to place a utility when docking it into a document tab. A null Address docks into the active
/// document's section and appends the tab. A non-null Address targets a specific section and tab order.
/// Activate selects the docked tab and makes it the active document.
/// </summary>
public record DockUtilityPlacement(DocumentAddress? Address, bool Activate);

public sealed partial class DocumentsPanel : UserControl, IDocumentsPanel
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDocumentsLogger _logger;
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IWindowModeService _windowModeService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWebViewFocusRegistry _webViewFocusRegistry;
    private readonly IFocusService _focusService;
    private readonly Dictionary<DocumentArea, DocumentToolbar> _areaToolbars = new();

    private bool _isShuttingDown = false;

    public DocumentsPanelViewModel ViewModel { get; }

    public IReadOnlyList<DocumentSectionId> VisibleSections => SectionContainer.VisibleSections;

    public ResourceKey ActiveDocument
    {
        get => SectionContainer.ActiveDocument;
        set => SectionContainer.SetActiveDocument(value);
    }

    public DocumentsPanel(
        IServiceProvider serviceProvider,
        IDocumentsLogger logger,
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

        ViewModel = serviceProvider.AcquireService<DocumentsPanelViewModel>();

        this.DataContext = ViewModel;

        // Wire up section container events
        SectionContainer.ActiveDocumentChanged += OnActiveDocumentChanged;
        SectionContainer.DocumentsLayoutChanged += OnSectionDocumentsLayoutChanged;
        SectionContainer.CloseRequested += OnSectionCloseRequested;
        SectionContainer.ContextMenuActionRequested += OnSectionContextMenuActionRequested;
        SectionContainer.AreaLayoutChanged += OnAreaLayoutChanged;
        SectionContainer.AreaSizeChanged += OnAreaSizeChanged;
        SectionContainer.AreaSizeResetRequested += OnAreaSizeResetRequested;
        SectionContainer.AreaSplitAvailabilityChanged += OnAreaSplitAvailabilityChanged;
        SectionContainer.FilesDropped += OnSectionFilesDropped;

        SectionContainer.InitializeTabDrag(TabDragOverlay);
        ConfigureResourceDropTarget();

        CreateAreaToolbars();

        Loaded += DocumentsPanel_Loaded;
        Unloaded += DocumentsPanel_Unloaded;
    }

    private void OnActiveDocumentChanged(ResourceKey documentResource)
    {
        if (_isShuttingDown)
        {
            return;
        }

        ViewModel.OnActiveDocumentChanged(documentResource);
    }

    private void OnSectionDocumentsLayoutChanged(DocumentSection section, List<ResourceKey> documents)
    {
        if (_isShuttingDown)
        {
            return;
        }

        ViewModel.OnDocumentLayoutChanged();
    }

    private void OnSectionCloseRequested(DocumentSection section, ResourceKey fileResource)
    {
        // A docked utility is never destroyed: closing its tab docks it back into the Utility Panel instead.
        ViewModel.OnCloseDocumentRequested(fileResource);
    }

    private void OnSectionContextMenuActionRequested(DocumentSection section, DocumentTab tab, DocumentTabMenuAction action)
    {
        OnDocumentTabContextMenuAction(tab, action);
    }

    // Builds the per-area toolbar hosted in each area's tab strip footer. Main gets a split button only;
    // the collapsible areas also get a close button.
    private void CreateAreaToolbars()
    {
        foreach (var area in DocumentLayoutHelper.AllAreas)
        {
            var toolbar = new DocumentToolbar(area);
            toolbar.SplitChangeRequested += OnToolbarSplitChangeRequested;
            toolbar.CloseAreaRequested += OnToolbarCloseAreaRequested;

            _areaToolbars[area] = toolbar;
            SectionContainer.SetAreaToolbar(area, toolbar);
        }
    }

    private void OnAreaLayoutChanged(DocumentArea area, bool isSplit, double splitRatio)
    {
        if (_areaToolbars.TryGetValue(area, out var toolbar))
        {
            toolbar.UpdateSplitState(isSplit);
        }

        if (_isShuttingDown)
        {
            return;
        }

        ViewModel.OnAreaLayoutChanged(area, isSplit, splitRatio);
    }

    private void OnAreaSizeChanged(DocumentArea area, double size)
    {
        if (_isShuttingDown)
        {
            return;
        }

        ViewModel.StoreAreaSize(area, (float)size);
    }

    private void OnAreaSizeResetRequested(DocumentArea area)
    {
        ViewModel.ResetAreaSize(area);
    }

    private void OnAreaSplitAvailabilityChanged(DocumentArea area, bool canSplit)
    {
        if (_areaToolbars.TryGetValue(area, out var toolbar))
        {
            toolbar.UpdateSplitAvailable(canSplit);
        }
    }

    private void OnToolbarSplitChangeRequested(DocumentArea area, bool isSplit)
    {
        SectionContainer.SetAreaSplit(area, isSplit);
    }

    private void OnToolbarCloseAreaRequested(DocumentArea area)
    {
        ViewModel.SetAreaVisible(area, false);
    }

    private void OnSectionFilesDropped(DocumentSection targetSection, List<IResource> resources, int insertionSlot)
    {
        // The built-in drag-and-drop path: the section maps the drop point to an insertion slot. The
        // pointer-driven path arrives through TryDrop instead, which carries the divider's insertion slot.
        _ = HandleDroppedFiles(targetSection, resources, insertionSlot);
    }

    // The insertion slot is where the drop landed in the target section's tab order. The open is awaited
    // so focus can transfer to the resulting document once its view exists. The command queue serializes
    // the opens either way, so this does not change the order documents open in.
    private async Task HandleDroppedFiles(DocumentSection targetSection, List<IResource> resources, int insertionSlot)
    {
        if (_isShuttingDown)
        {
            return;
        }

        var targetSectionId = targetSection.SectionId;
        int droppedFileOffset = 0;
        ResourceKey? documentToFocus = null;

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
                var existingSection = existingLocation.Section;
                var existingTab = existingLocation.Tab;

                // Already open - move to the target section, otherwise reorder within it, then select it
                if (existingSection.SectionId != targetSectionId)
                {
                    SectionContainer.MoveTabToSection(existingTab, targetSectionId, slot);
                }
                else
                {
                    existingSection.ReorderTab(existingTab, slot);
                    existingSection.SelectTab(existingTab);
                    SectionContainer.ActivateDocument(fileResourceKey, targetSectionId);
                }
            }
            else
            {
                // Not open - use the command to open in the target section at the divider slot
                await _commandService.ExecuteAsync<IOpenDocumentCommand>(command =>
                {
                    command.FileResource = fileResourceKey;
                    command.TargetSection = targetSectionId;
                    command.TargetTabIndex = slot;
                });
            }

            documentToFocus = fileResourceKey;
        }

        // On the pointer-drag head, dropping a resource into the document area is a deliberate move into that
        // area, so hand keyboard focus to the resulting document, matching a tab click. The built-in
        // drag-and-drop head (where the coordinator is absent) keeps its own focus behaviour. Skipped when
        // nothing opened (a folder-only drop).
        if (_resourceDragCoordinator is not null
            && documentToFocus is ResourceKey keyToFocus)
        {
            FocusActivatedDocument(keyToFocus);
        }
    }

    // Gives the active document keyboard focus where no interaction carries focus to it: a workspace restore
    // selecting its tab, and a layout-mode change that collapses the panels. Without this the panel that holds
    // the keyboard keeps it while the document looks focused.
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

    private void DocumentsPanel_Loaded(object sender, RoutedEventArgs e)
    {
        // Listen for layout mode changes to show/hide the tab strip in Presentation mode
        _messengerService.Register<LayoutModeChangedMessage>(this, OnLayoutModeChanged);

        // The collapsible areas follow the workspace region visibility.
        _messengerService.Register<RegionVisibilityChangedMessage>(this, OnRegionVisibilityChanged);

        // Area sizes are restored and reset through the workspace settings facade.
        ViewModel.AreaSizeChanged += OnStoredAreaSizeChanged;
        ApplyStoredAreaSizes();
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

        // Apply initial tab strip visibility based on the current layout mode
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

        if (_focusService.FocusedPanel != WorkspacePanel.Documents)
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
            SectionContainer.ActivateDocument(message.DocumentResource, location.Section.SectionId);
        }
    }

    private void OnResetLayoutRequested(object recipient, ResetLayoutRequestedMessage message)
    {
        if (_isShuttingDown)
        {
            return;
        }

        // Fold every area back to a single section at its default split position
        _ = SectionContainer.ResetAreaLayoutAsync();
    }

    private void DocumentsPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        UnregisterAsResourceDropTarget();
        ViewModel.AreaSizeChanged -= OnStoredAreaSizeChanged;
        ViewModel.OnViewUnloaded();
        _messengerService.UnregisterAll(this);
    }

    private void OnLayoutModeChanged(object recipient, LayoutModeChangedMessage message)
    {
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

    private void UpdateTabStripVisibility(LayoutMode layoutMode)
    {
        // In Presentation mode, hide the tab strip and toolbar to show only the document content.
        // In all other modes, show the tab strip and toolbar.
        bool showTabStrip = layoutMode != LayoutMode.Presentation;
        SectionContainer.UpdateTabStripVisibility(showTabStrip);

        foreach (var toolbar in _areaToolbars.Values)
        {
            toolbar.Visibility = showTabStrip ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnRegionVisibilityChanged(object recipient, RegionVisibilityChangedMessage message)
    {
        if (_isShuttingDown)
        {
            return;
        }

        ApplyAreaVisibility();
    }

    // Shows or hides the collapsible areas to match the workspace region visibility. Hiding an area
    // leaves its tabs in place, so the documents reappear where they were when it is shown again.
    private void ApplyAreaVisibility()
    {
        SectionContainer.SetAreaVisible(DocumentArea.Bottom, ViewModel.IsAreaVisible(DocumentArea.Bottom));
        SectionContainer.SetAreaVisible(DocumentArea.Side, ViewModel.IsAreaVisible(DocumentArea.Side));

        ApplyStoredAreaSizes();
    }

    private void ApplyStoredAreaSizes()
    {
        SectionContainer.SetAreaSize(DocumentArea.Bottom, ViewModel.GetAreaSize(DocumentArea.Bottom));
        SectionContainer.SetAreaSize(DocumentArea.Side, ViewModel.GetAreaSize(DocumentArea.Side));
    }

    private void OnStoredAreaSizeChanged(DocumentArea area)
    {
        SectionContainer.SetAreaSize(area, ViewModel.GetAreaSize(area));
    }

    // Resolves a requested section to one that currently holds tabs. A secondary section whose area is
    // not split folds into that area's primary section. A section in a collapsed area is left alone: the
    // area keeps its tabs while hidden.
    private DocumentSectionId ResolveMountedSection(DocumentSectionId sectionId)
    {
        var area = sectionId.GetArea();
        if (sectionId.IsSecondarySection()
            && !SectionContainer.IsAreaSplit(area))
        {
            return area.GetPrimarySection();
        }

        return sectionId;
    }

    public bool IsAreaSplit(DocumentArea area)
    {
        return SectionContainer.IsAreaSplit(area);
    }

    public void SetAreaSplit(DocumentArea area, bool isSplit)
    {
        SectionContainer.SetAreaSplit(area, isSplit);
    }

    public double GetAreaSplitRatio(DocumentArea area)
    {
        return SectionContainer.GetAreaSplitRatio(area);
    }

    public void SetAreaSplitRatio(DocumentArea area, double ratio)
    {
        SectionContainer.SetAreaSplitRatio(area, ratio);
    }

    public async Task ResetAreaLayoutAsync()
    {
        await SectionContainer.ResetAreaLayoutAsync();
    }

    public IReadOnlyList<OpenDocumentInfo> GetOpenDocuments()
    {
        var documents = new List<OpenDocumentInfo>();

        // Includes sections in a collapsed area, which keep their documents open.
        foreach (var section in SectionContainer.GetAllSections())
        {
            int tabOrder = 0;
            foreach (var tab in section.GetAllTabs())
            {
                var address = new DocumentAddress(
                    WindowIndex: 0,
                    Section: section.SectionId,
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

        // Resolve the target section from the address, defaulting to the active section. An address
        // naming a section whose area is not split folds into that area's primary section.
        var address = effectiveOptions.Address;
        var sectionId = address is not null ? ResolveMountedSection(address.Section) : SectionContainer.ActiveSectionId;

        // Check if the file is already opened in any section
        var existingLocation = SectionContainer.FindDocumentTab(fileResource);
        if (existingLocation is not null)
        {
            var existingSection = existingLocation.Section;
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

                existingSection.RemoveTab(existingTab);
                NotifyLayoutChanged();

                var tabOrder = effectiveOptions.Address?.TabOrder ?? 0;
                var reopenAddress = new DocumentAddress(WindowIndex: 0, Section: sectionId, TabOrder: tabOrder);
                var reopenOptions = effectiveOptions with { Address = reopenAddress };
                return await OpenDocument(fileResource, reopenOptions);
            }

            // Without an explicit address the existing tab stays in its own
            // section. Moving it to wherever the active section happens to be
            // would yank it from under the user.
            if (address is null)
            {
                sectionId = existingSection.SectionId;
            }

            // If a different section was explicitly requested, move it there.
            if (existingSection.SectionId != sectionId)
            {
                SectionContainer.MoveTabToSection(existingTab, sectionId);
            }

            if (effectiveOptions.Activate)
            {
                var targetSection = SectionContainer.GetSection(sectionId);
                targetSection.SelectTab(existingTab);
                SectionContainer.ActivateDocument(fileResource, sectionId);
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

            if (effectiveOptions.Activate)
            {
                FocusActivatedDocument(fileResource);
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
        var targetSectionForNew = SectionContainer.GetSection(sectionId);

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
            targetSectionForNew.RemoveTab(documentTab);
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
            SectionContainer.ActivateDocument(fileResource, sectionId);
        }

        if (!string.IsNullOrEmpty(effectiveOptions.Location))
        {
            await NavigateToLocation(fileResource, effectiveOptions.Location);
        }

        if (!string.IsNullOrEmpty(effectiveOptions.EditorStateJson))
        {
            await documentView.RestoreEditorStateAsync(effectiveOptions.EditorStateJson);
        }

        if (effectiveOptions.Activate)
        {
            FocusActivatedDocument(fileResource);
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
            var section = location.Section;
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
                int tabIndex = section.GetTabIndex(documentTab);

                // Handle selection of next document before removing the tab
                SectionContainer.HandleDocumentClosing(fileResource, section.SectionId, tabIndex);

                section.RemoveTab(documentTab);

                // Update all tab names since closing a tab may resolve filename ambiguity
                UpdateAllTabDisplayNames();
            }

            return Result.Ok();
        }

        // We failed to find any open document for this fileResource, but this is the
        // state we were trying to get into anyway, so we consider this a success.

        return Result.Ok();
    }

    /// <summary>
    /// Docks a utility into a document tab: creates a tab hosting the utility's borrowed controller (reusing its
    /// live WebView) and stamps the utility tab metadata. The controller's WebView is reparented into the tab
    /// once it is in the visual tree.
    /// </summary>
    public Result DockUtility(CustomUtilityView panelView, DockUtilityPlacement placement)
    {
        var resource = panelView.FileResource;
        var editorId = panelView.UtilityId;

        var resolveResult = ViewModel.ResolveResourcePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for utility resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var filePath = resolveResult.Value;

        var address = placement.Address;
        var sectionId = address is not null ? ResolveMountedSection(address.Section) : SectionContainer.ActiveSectionId;
        var section = SectionContainer.GetSection(sectionId);

        var documentTab = new DocumentTab();
        documentTab.ViewModel.FileResource = resource;
        documentTab.ViewModel.FilePath = filePath;
        documentTab.ViewModel.EditorId = editorId;
        ApplyUtilityTabMetadata(documentTab, editorId);

        var dockedView = new DockedUtilityDocumentView(_serviceProvider, _messengerService, panelView.Controller);
        dockedView.EditorId = editorId;
        dockedView.Bind(resource, filePath);

        if (address is not null)
        {
            section.InsertTab(documentTab, address.TabOrder);
        }
        else
        {
            section.AddTab(documentTab);
        }

        // No open announcement: a utility is presented by docking, never opened as a document, and the
        // documents service refuses to open one. The view model suppresses the matching close.
        documentTab.ViewModel.DocumentView = dockedView;
        documentTab.Content = dockedView;

        if (placement.Activate)
        {
            section.SelectTab(documentTab);
        }

        // Reparent the borrowed WebView into the tab now that the tab is in the visual tree.
        dockedView.Dock();

        if (placement.Activate)
        {
            SectionContainer.ActivateDocument(resource, sectionId);
        }

        return Result.Ok();
    }

    /// <summary>
    /// Activates the open document tab for a docked utility (used when its rail button is clicked).
    /// </summary>
    public void ActivateUtilityTab(ResourceKey resource)
    {
        var location = SectionContainer.FindDocumentTab(resource);
        if (location is not null)
        {
            SectionContainer.ActivateDocument(resource, location.Section.SectionId);
        }
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

    /// <summary>
    /// Removes a docked utility's document tab. The caller reparents the controller's WebView back to the
    /// Utility Panel first, so the tab (and its now-empty docked view) is dropped without any teardown.
    /// </summary>
    public void RemoveUtilityTab(ResourceKey resource)
    {
        var location = SectionContainer.FindDocumentTab(resource);
        if (location is null)
        {
            return;
        }

        var section = location.Section;
        var documentTab = location.Tab;

        int tabIndex = section.GetTabIndex(documentTab);
        SectionContainer.HandleDocumentClosing(resource, section.SectionId, tabIndex);

        _ = documentTab.ViewModel.DocumentView?.PrepareToClose();
        section.RemoveTab(documentTab);

        UpdateAllTabDisplayNames();
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

        foreach (var section in SectionContainer.GetAllSections())
        {
            foreach (var documentTab in section.GetAllTabs())
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
        SectionContainer.ActivateDocument(fileResource, location.Section.SectionId);
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

        var section = location.Section;
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
            if (section.GetSelectedDocument() == oldResource)
            {
                section.RefreshSelectedTab();
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

    private void ApplyUtilityTabMetadata(DocumentTab documentTab, EditorId editorId)
    {
        var utilityInfo = ViewModel.ResolveUtilityTabInfo(editorId);
        if (utilityInfo is null)
        {
            return;
        }

        documentTab.ViewModel.IsUtility = true;
        documentTab.ViewModel.UtilityIconName = utilityInfo.IconName;
        documentTab.ViewModel.DocumentName = utilityInfo.Title;
        documentTab.ViewModel.UtilityTooltip = utilityInfo.Tooltip;
    }

    private void UpdateAllTabDisplayNames()
    {
        // Collect all tabs from all sections. Utility tabs keep their manifest title, so they are
        // excluded from filename-based disambiguation.
        var allTabs = new List<DocumentTab>();
        foreach (var section in SectionContainer.GetAllSections())
        {
            allTabs.AddRange(section.GetAllTabs().Where(tab => !tab.ViewModel.IsUtility));
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

    private void OnDocumentTabContextMenuAction(DocumentTab tab, DocumentTabMenuAction action)
    {
        switch (action)
        {
            case DocumentTabMenuAction.Close:
                CloseTab(tab);
                break;
            case DocumentTabMenuAction.CloseOthers:
                CloseOtherTabs(tab);
                break;
            case DocumentTabMenuAction.CloseOthersRight:
                CloseOtherTabsRight(tab);
                break;
            case DocumentTabMenuAction.CloseOthersLeft:
                CloseOtherTabsLeft(tab);
                break;
            case DocumentTabMenuAction.CloseAll:
                CloseAllTabs(tab);
                break;
            case DocumentTabMenuAction.MoveToPrimarySection:
                MoveTabWithinArea(tab, toSecondarySection: false);
                break;
            case DocumentTabMenuAction.MoveToSecondarySection:
                MoveTabWithinArea(tab, toSecondarySection: true);
                break;
            case DocumentTabMenuAction.CopyResourceKey:
                CopyResourceKeyForTab(tab);
                break;
            case DocumentTabMenuAction.CopyFilePath:
                CopyFilePathForTab(tab);
                break;
            case DocumentTabMenuAction.SelectFile:
                SelectFileForTab(tab);
                break;
            case DocumentTabMenuAction.OpenFileExplorer:
                OpenFileExplorerForTab(tab);
                break;
            case DocumentTabMenuAction.OpenApplication:
                OpenApplicationForTab(tab);
                break;
            case DocumentTabMenuAction.RestoreChrome:
                RestoreChromeForTab(tab);
                break;
            case DocumentTabMenuAction.Reopen:
                _ = ReopenTab(tab);
                break;
            case DocumentTabMenuAction.ReopenWith:
                _ = ReopenTabWithDialog(tab);
                break;
        }
    }

    private void CloseTab(DocumentTab tab)
    {
        var fileResource = tab.ViewModel.FileResource;
        ViewModel.OnCloseDocumentRequested(fileResource);
    }

    // Moves a tab between the two sections of its own area. Moving between areas is a drag.
    private void MoveTabWithinArea(DocumentTab tab, bool toSecondarySection)
    {
        var area = tab.SectionId.GetArea();
        if (!SectionContainer.IsAreaSplit(area))
        {
            return;
        }

        var targetSection = toSecondarySection
            ? area.GetSecondarySection()
            : area.GetPrimarySection();

        if (SectionContainer.MoveTabToSection(tab, targetSection))
        {
            UpdateAllTabDisplayNames();
            NotifyLayoutChanged();
        }
    }

    private void NotifyLayoutChanged()
    {
        ViewModel.OnDocumentLayoutChanged();
    }

    private void CloseOtherTabs(DocumentTab keepTab)
    {
        // Find which section contains the tab to keep
        var location = SectionContainer.FindDocumentTab(keepTab.ViewModel.FileResource);
        if (location is null)
        {
            return;
        }

        var section = location.Section;

        var tabsToClose = new List<ResourceKey>();

        // Only close other tabs within the same section.
        foreach (var documentTab in section.GetAllTabs())
        {
            if (documentTab != keepTab)
            {
                tabsToClose.Add(documentTab.ViewModel.FileResource);
            }
        }

        foreach (var fileResource in tabsToClose)
        {
            ViewModel.OnCloseDocumentRequested(fileResource);
        }
    }

    private void CloseOtherTabsRight(DocumentTab referenceTab)
    {
        // Find which section contains the reference tab
        var location = SectionContainer.FindDocumentTab(referenceTab.ViewModel.FileResource);
        if (location is null)
        {
            return;
        }

        var section = location.Section;

        var tabsToClose = new List<ResourceKey>();
        bool foundReference = false;

        // Close tabs to the right within the same section.
        foreach (var documentTab in section.GetAllTabs())
        {
            if (foundReference)
            {
                tabsToClose.Add(documentTab.ViewModel.FileResource);
            }
            if (documentTab == referenceTab)
            {
                foundReference = true;
            }
        }

        foreach (var fileResource in tabsToClose)
        {
            ViewModel.OnCloseDocumentRequested(fileResource);
        }
    }

    private void CloseOtherTabsLeft(DocumentTab referenceTab)
    {
        // Find which section contains the reference tab
        var location = SectionContainer.FindDocumentTab(referenceTab.ViewModel.FileResource);
        if (location is null)
        {
            return;
        }

        var section = location.Section;

        var tabsToClose = new List<ResourceKey>();

        // Close tabs to the left within the same section.
        foreach (var documentTab in section.GetAllTabs())
        {
            if (documentTab == referenceTab)
            {
                break;
            }
            tabsToClose.Add(documentTab.ViewModel.FileResource);
        }

        foreach (var fileResource in tabsToClose)
        {
            ViewModel.OnCloseDocumentRequested(fileResource);
        }
    }

    private void CloseAllTabs(DocumentTab referenceTab)
    {
        // Find which section contains the reference tab
        var location = SectionContainer.FindDocumentTab(referenceTab.ViewModel.FileResource);
        if (location is null)
        {
            return;
        }

        var section = location.Section;

        var tabsToClose = new List<ResourceKey>();

        // Only close tabs within the same section.
        foreach (var documentTab in section.GetAllTabs())
        {
            tabsToClose.Add(documentTab.ViewModel.FileResource);
        }

        foreach (var fileResource in tabsToClose)
        {
            ViewModel.OnCloseDocumentRequested(fileResource);
        }
    }

    private void SelectFileForTab(DocumentTab tab)
    {
        ViewModel.SelectFileForTab(tab.ViewModel.FileResource);
    }

    private void CopyResourceKeyForTab(DocumentTab tab)
    {
        ViewModel.CopyResourceKeyForTab(tab.ViewModel.FileResource);
    }

    private void CopyFilePathForTab(DocumentTab tab)
    {
        ViewModel.CopyFilePathForTab(tab.ViewModel.FilePath);
    }

    private void OpenFileExplorerForTab(DocumentTab tab)
    {
        ViewModel.OpenFileExplorerForTab(tab.ViewModel.FileResource);
    }

    private void OpenApplicationForTab(DocumentTab tab)
    {
        ViewModel.OpenApplicationForTab(tab.ViewModel.FileResource);
    }

    private void RestoreChromeForTab(DocumentTab tab)
    {
        if (tab.Content is IDocumentChromeOwner chromeOwner)
        {
            chromeOwner.RestoreChrome();
        }
    }

    private Task ReopenTab(DocumentTab tab)
    {
        // Reopen using the current editor (no dialog)
        return ReopenTabWithEditor(tab, tab.ViewModel.EditorId);
    }

    private async Task ReopenTabWithDialog(DocumentTab tab)
    {
        var fileResource = tab.ViewModel.FileResource;

        var selectedEditorId = tab.ViewModel.EditorId;

        var pickList = ViewModel.GetEditorPickList(fileResource, tab.ViewModel.EditorId);
        if (pickList is not null)
        {
            // Multiple editors available, show choice dialog.
            var title = _stringLocalizer.GetString("OpenWithDialog_Title");
            var message = _stringLocalizer.GetString("OpenWithDialog_Message");

            var choiceResult = await _dialogService.ShowChoiceDialogAsync(
                title, message, pickList.Labels, pickList.SelectedIndex, checkbox: null);
            if (choiceResult.IsFailure)
            {
                return;
            }

            selectedEditorId = pickList.EditorIds[choiceResult.Value.SelectedIndex];

            await ViewModel.SetPreferredEditorAsync(fileResource, selectedEditorId);
        }

        await ReopenTabWithEditor(tab, selectedEditorId);
    }

    private async Task ReopenTabWithEditor(DocumentTab tab, EditorId editorId)
    {
        var fileResource = tab.ViewModel.FileResource;

        // Capture state before closing so we can restore it after reopening
        var sectionId = tab.SectionId;
        var currentLocation = SectionContainer.FindDocumentTab(fileResource);
        var tabIndex = currentLocation?.Section.GetTabIndex(tab) ?? 0;

        string? editorState = null;
        if (tab.ViewModel.DocumentView is not null)
        {
            editorState = await tab.ViewModel.DocumentView.TrySaveEditorStateAsync();
        }

        // Close then reopen via the command service, which processes them sequentially
        var closeResult = await _commandService.ExecuteAsync<ICloseDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
        });

        if (closeResult.IsFailure)
        {
            return;
        }

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
            command.EditorId = editorId;
            command.EditorStateJson = editorState;
            command.TargetSection = sectionId;
            command.TargetTabIndex = tabIndex;
        });
    }
}
