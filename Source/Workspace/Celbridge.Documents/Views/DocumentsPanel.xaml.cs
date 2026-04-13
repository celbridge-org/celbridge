using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Dialog;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Views;

using IDocumentsLogger = Logging.ILogger<DocumentsPanel>;

public sealed partial class DocumentsPanel : UserControl, IDocumentsPanel
{
    private readonly IDocumentsLogger _logger;
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IWindowModeService _windowModeService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    private bool _isShuttingDown = false;

    public DocumentsPanelViewModel ViewModel { get; }

    /// <summary>
    /// Gets or sets the current number of document sections (1-3).
    /// </summary>
    public int SectionCount
    {
        get => SectionContainer.SectionCount;
        set => SectionContainer.SetSectionCount(value);
    }

    /// <summary>
    /// Gets or sets the active document - the document being inspected and where new documents open.
    /// </summary>
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
        IWorkspaceWrapper workspaceWrapper,
        IWindowModeService windowModeService,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer)
    {
        InitializeComponent();

        _logger = logger;
        _messengerService = messengerService;
        _commandService = commandService;
        _windowModeService = windowModeService;
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;

        ViewModel = serviceProvider.AcquireService<DocumentsPanelViewModel>();

        //
        // Set the data context
        //
        this.DataContext = ViewModel;

        // Wire up section container events
        SectionContainer.ActiveDocumentChanged += OnActiveDocumentChanged;
        SectionContainer.DocumentsLayoutChanged += OnSectionDocumentsLayoutChanged;
        SectionContainer.CloseRequested += OnSectionCloseRequested;
        SectionContainer.ContextMenuActionRequested += OnSectionContextMenuActionRequested;
        SectionContainer.SectionCountChanged += OnSectionCountChanged;
        SectionContainer.SectionRatiosChanged += OnSectionRatiosChanged;
        SectionContainer.FilesDropped += OnSectionFilesDropped;

        // Wire up toolbar events
        DocumentToolbar.SectionCountChangeRequested += OnToolbarSectionCountChangeRequested;

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
        ViewModel.OnCloseDocumentRequested(fileResource);
    }

    private void OnSectionContextMenuActionRequested(DocumentSection section, DocumentTab tab, DocumentTabMenuAction action)
    {
        OnDocumentTabContextMenuAction(tab, action);
    }

    private void OnSectionCountChanged(int newCount)
    {
        // Update toolbar to reflect new section count
        DocumentToolbar.UpdateSectionCount(newCount);
        // Note: Ratios are persisted via OnSectionRatiosChanged which fires after count changes
    }

    private void OnSectionRatiosChanged(List<double> ratios)
    {
        // Notify the ViewModel to persist the section ratios
        // The section count is inferred from the ratios list length
        ViewModel.OnSectionRatiosChanged(ratios);
    }

    private void OnSectionFilesDropped(DocumentSection targetSection, List<IResource> resources)
    {
        HandleDroppedFiles(targetSection, resources);
    }

    private void HandleDroppedFiles(DocumentSection targetSection, List<IResource> resources)
    {
        if (_isShuttingDown)
        {
            return;
        }

        var targetSectionIndex = targetSection.SectionIndex;

        foreach (var resource in resources)
        {
            if (resource is not IFileResource fileResource)
            {
                continue;
            }

            var fileResourceKey = ViewModel.GetResourceKey(fileResource);

            // Check if the file is already open in any section
            var (existingSection, existingTab) = SectionContainer.FindDocumentTab(fileResourceKey);
            if (existingTab != null && existingSection != null)
            {
                // Already open - move to target section if different, otherwise just select it
                if (existingSection.SectionIndex != targetSectionIndex)
                {
                    SectionContainer.MoveTabToSection(existingTab, targetSectionIndex);
                }
                else
                {
                    existingSection.SelectTab(existingTab);
                    SectionContainer.ActivateDocument(fileResourceKey, targetSectionIndex);
                }
            }
            else
            {
                // Not open - use the command to open in the target section
                _commandService.Execute<IOpenDocumentCommand>(command =>
                {
                    command.FileResource = fileResourceKey;
                    command.TargetSectionIndex = targetSectionIndex;
                });
            }
        }
    }

    private void OnToolbarSectionCountChangeRequested(int requestedCount)
    {
        SectionContainer.SetSectionCount(requestedCount);
    }

    private void DocumentsPanel_Loaded(object sender, RoutedEventArgs e)
    {
        // Listen for window mode changes to show/hide tab strip in Presenter mode
        _messengerService.Register<WindowModeChangedMessage>(this, OnWindowModeChanged);

        // Listen for document view focus to update active document
        _messengerService.Register<DocumentViewFocusedMessage>(this, OnDocumentViewFocused);

        // Listen for layout reset requests to reset section count
        _messengerService.Register<ResetLayoutRequestedMessage>(this, OnResetLayoutRequested);

        // Apply initial tab strip visibility based on current window mode
        UpdateTabStripVisibility(_windowModeService.WindowMode);
    }

    private void OnDocumentViewFocused(object recipient, DocumentViewFocusedMessage message)
    {
        if (_isShuttingDown)
        {
            return;
        }

        // Find the section containing this document and update the active document
        var (section, _) = SectionContainer.FindDocumentTab(message.DocumentResource);
        if (section != null)
        {
            SectionContainer.ActivateDocument(message.DocumentResource, section.SectionIndex);
        }
    }

    private void OnResetLayoutRequested(object recipient, ResetLayoutRequestedMessage message)
    {
        if (_isShuttingDown)
        {
            return;
        }

        // Reset to single section
        if (SectionCount > 1)
        {
            SectionContainer.SetSectionCount(1);
        }

        // Reset section ratios
        _ = SectionContainer.ResetSectionRatiosAsync();
    }

    private void DocumentsPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnViewUnloaded();
        _messengerService.UnregisterAll(this);
    }

    private void OnWindowModeChanged(object recipient, WindowModeChangedMessage message)
    {
        UpdateTabStripVisibility(message.WindowMode);
    }

    private void UpdateTabStripVisibility(WindowMode windowMode)
    {
        // In Presenter mode, hide the tab strip and toolbar to show only the document content
        // In all other modes, show the tab strip and toolbar
        bool showTabStrip = windowMode != WindowMode.Presenter;
        SectionContainer.UpdateTabStripVisibility(showTabStrip);
        DocumentToolbar.Visibility = showTabStrip ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetSectionRatios(List<double> ratios)
    {
        SectionContainer.SetSectionRatios(ratios);
    }

    public async Task ResetSectionRatiosAsync()
    {
        await SectionContainer.ResetSectionRatiosAsync();
    }

    public IReadOnlyList<OpenDocumentInfo> GetOpenDocuments()
    {
        var documents = new List<OpenDocumentInfo>();

        for (int sectionIndex = 0; sectionIndex < SectionContainer.SectionCount; sectionIndex++)
        {
            var section = SectionContainer.GetSection(sectionIndex);
            int tabOrder = 0;
            foreach (var tab in section.GetAllTabs())
            {
                var address = new DocumentAddress(
                    WindowIndex: 0,
                    SectionIndex: sectionIndex,
                    TabOrder: tabOrder++);

                documents.Add(new OpenDocumentInfo(
                    tab.ViewModel.FileResource,
                    address,
                    tab.ViewModel.EditorId));
            }
        }

        return documents;
    }

    public async Task<Result> OpenDocument(ResourceKey fileResource, OpenDocumentOptions? options = null)
    {
        var effectiveOptions = options ?? new OpenDocumentOptions();

        // Resolve the target section from the address, defaulting to the active section
        var address = effectiveOptions.Address;
        int sectionIndex = address is not null ? address.SectionIndex : SectionContainer.ActiveSectionIndex;
        if (sectionIndex < 0 || sectionIndex >= SectionContainer.SectionCount)
        {
            sectionIndex = 0;
        }

        // Check if the file is already opened in any section
        var (existingSection, existingTab) = SectionContainer.FindDocumentTab(fileResource);
        if (existingTab != null && existingSection != null)
        {
            // If a different editor was requested, close and reopen with the new editor
            if (!effectiveOptions.EditorId.IsEmpty &&
                !existingTab.ViewModel.EditorId.IsEmpty &&
                effectiveOptions.EditorId != existingTab.ViewModel.EditorId)
            {
                var closeResult = await existingTab.ViewModel.CloseDocument(forceClose: false);
                if (closeResult.IsFailure || !closeResult.Value)
                {
                    return Result.Ok();
                }

                existingSection.RemoveTab(existingTab);
                NotifyLayoutChanged();

                var reopenAddress = new DocumentAddress(WindowIndex: 0, SectionIndex: sectionIndex, TabOrder: 0);
                var reopenOptions = effectiveOptions with { Address = reopenAddress };
                return await OpenDocument(fileResource, reopenOptions);
            }

            // If already open in a different section, move it
            if (existingSection.SectionIndex != sectionIndex)
            {
                SectionContainer.MoveTabToSection(existingTab, sectionIndex);
            }

            if (effectiveOptions.Activate)
            {
                var targetSection = SectionContainer.GetSection(sectionIndex);
                targetSection.SelectTab(existingTab);
                SectionContainer.ActivateDocument(fileResource, sectionIndex);
            }

            if (effectiveOptions.ForceReload)
            {
                var reloadResult = await existingTab.ViewModel.ReloadDocument();
                if (reloadResult.IsFailure)
                {
                    return Result.Fail($"Failed to reload document: {fileResource}")
                        .WithErrors(reloadResult);
                }
            }

            if (!string.IsNullOrEmpty(effectiveOptions.Location))
            {
                await NavigateToLocation(fileResource, effectiveOptions.Location);
            }

            return Result.Ok();
        }

        // Resolve the file path from the resource key
        var resourceRegistry = ServiceLocator.AcquireService<IWorkspaceWrapper>()
            .WorkspaceService.ResourceService.Registry;
        var resolveResult = resourceRegistry.ResolveResourcePath(fileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{fileResource}'")
                .WithErrors(resolveResult);
        }
        var filePath = resolveResult.Value;

        // Open in the specified section
        var targetSectionForNew = SectionContainer.GetSection(sectionIndex);

        var documentTab = new DocumentTab();
        documentTab.ViewModel.FileResource = fileResource;
        documentTab.ViewModel.FilePath = filePath;
        documentTab.ViewModel.DocumentName = fileResource.ResourceName;

        targetSectionForNew.AddTab(documentTab);

        if (effectiveOptions.Activate)
        {
            targetSectionForNew.SelectTab(documentTab);
        }

        var createResult = await ViewModel.CreateDocumentView(fileResource, effectiveOptions.EditorId);
        if (createResult.IsFailure)
        {
            targetSectionForNew.RemoveTab(documentTab);
            return Result.Fail($"Failed to create document view for file resource: '{fileResource}'")
                .WithErrors(createResult);
        }
        var documentView = createResult.Value;

        documentTab.ViewModel.DocumentView = documentView;
        documentTab.Content = documentView;

        UpdateEditorDisplayName(documentTab, effectiveOptions.EditorId);

        targetSectionForNew.RefreshSelectedTab();
        UpdateAllTabDisplayNames();

        if (effectiveOptions.Activate)
        {
            SectionContainer.ActivateDocument(fileResource, sectionIndex);
        }

        if (!string.IsNullOrEmpty(effectiveOptions.Location))
        {
            await NavigateToLocation(fileResource, effectiveOptions.Location);
        }

        if (!string.IsNullOrEmpty(effectiveOptions.EditorStateJson))
        {
            await documentView.RestoreEditorStateAsync(effectiveOptions.EditorStateJson);
        }

        return Result.Ok();
    }

    public async Task<Result> NavigateToLocation(ResourceKey fileResource, string location)
    {
        var (_, documentTab) = SectionContainer.FindDocumentTab(fileResource);
        if (documentTab != null)
        {
            var documentView = documentTab.Content as IDocumentView;
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
        var (section, documentTab) = SectionContainer.FindDocumentTab(fileResource);
        if (documentTab != null && section != null)
        {
            var closeResult = await documentTab.ViewModel.CloseDocument(forceClose);
            if (closeResult.IsFailure)
            {
                return Result.Fail($"An error occurred when closing the document for file resource: '{fileResource}'")
                    .WithErrors(closeResult);
            }

            var didClose = closeResult.Value;

            if (didClose)
            {
                // Get the tab index before removing it (needed for selecting next document)
                int tabIndex = section.GetTabIndex(documentTab);

                // Handle selection of next document before removing the tab
                SectionContainer.HandleDocumentClosing(fileResource, section.SectionIndex, tabIndex);

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

    public async Task<Result> SaveModifiedDocuments(double deltaTime)
    {
        int savedCount = 0;
        int pendingSaveCount = 0;
        List<ResourceKey> failedSaves = new();

        for (int i = 0; i < SectionContainer.SectionCount; i++)
        {
            var section = SectionContainer.GetSection(i);
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
                        // Make a note of the failed save and continue saving other documents
                        failedSaves.Add(documentTab.ViewModel.FileResource);
                    }
                    else
                    {
                        savedCount++;
                    }
                }
            }
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
        var (section, documentTab) = SectionContainer.FindDocumentTab(fileResource);
        if (documentTab != null && section != null)
        {
            // Selecting a tab will trigger section selection, which will update container activation
            section.SelectTab(documentTab);
            return Result.Ok();
        }

        return Result.Fail($"No opened document found for file resource: '{fileResource}'");
    }

    public IDocumentView? GetDocumentView(ResourceKey fileResource)
    {
        var (_, documentTab) = SectionContainer.FindDocumentTab(fileResource);
        return documentTab?.Content as IDocumentView;
    }

    public async Task<Result> ChangeDocumentResource(ResourceKey oldResource, DocumentViewType oldDocumentType, ResourceKey newResource, string newResourcePath, DocumentViewType newDocumentType)
    {
        // Find the document tab for the old resource
        var (section, documentTab) = SectionContainer.FindDocumentTab(oldResource);

        if (documentTab is null || section is null)
        {
            // The document isn't open, so we don't need to do anything
            return Result.Ok();
        }

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

            // Reload the content to ensure the document reflects the current file state
            // and entity data is properly synchronized after the resource move/rename.
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

            // Populate the tab content
            documentTab.ViewModel.DocumentView = newDocumentView;
            documentTab.Content = newDocumentView;

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

    /// <summary>
    /// Updates all tab display names to ensure tabs with the same filename are disambiguated.
    /// Tabs with unique filenames show just the filename; tabs with ambiguous filenames
    /// show additional path segments to differentiate them.
    /// </summary>
    private void UpdateEditorDisplayName(DocumentTab documentTab, DocumentEditorId documentEditorId = default)
    {
        var extension = Path.GetExtension(documentTab.ViewModel.FileResource.ToString()).ToLowerInvariant();
        var documentsService = ViewModel.GetDocumentsService();
        var editorRegistry = documentsService.DocumentEditorRegistry;
        var factories = editorRegistry.GetFactoriesForFileExtension(extension);

        // If a specific editor was used, look it up directly
        if (!documentEditorId.IsEmpty)
        {
            var factoryResult = editorRegistry.GetFactoryById(documentEditorId);
            if (factoryResult.IsSuccess)
            {
                documentTab.ViewModel.EditorId = factoryResult.Value.EditorId;
                if (factories.Count >= 2)
                {
                    documentTab.ViewModel.EditorDisplayName = factoryResult.Value.DisplayName;
                }
                return;
            }
        }

        // Fall back to priority-based resolution
        if (factories.Count >= 2)
        {
            var resourceRegistry = ServiceLocator.AcquireService<IWorkspaceWrapper>()
                .WorkspaceService.ResourceService.Registry;
            var resolveResult = resourceRegistry.ResolveResourcePath(documentTab.ViewModel.FileResource);
            var filePath = resolveResult.IsSuccess ? resolveResult.Value : string.Empty;

            foreach (var factory in factories)
            {
                if (factory.CanHandleResource(documentTab.ViewModel.FileResource, filePath))
                {
                    documentTab.ViewModel.EditorDisplayName = factory.DisplayName;
                    documentTab.ViewModel.EditorId = factory.EditorId;
                    break;
                }
            }
        }
        else if (factories.Count == 1)
        {
            documentTab.ViewModel.EditorId = factories[0].EditorId;
        }
    }

    private void UpdateAllTabDisplayNames()
    {
        // Collect all tabs from all sections
        var allTabs = new List<DocumentTab>();
        for (int i = 0; i < SectionContainer.SectionCount; i++)
        {
            var section = SectionContainer.GetSection(i);
            allTabs.AddRange(section.GetAllTabs());
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
                foreach (var kvp in disambiguatedNames)
                {
                    kvp.Key.ViewModel.DocumentName = kvp.Value;
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
            case DocumentTabMenuAction.MoveLeft:
                MoveTabLeft(tab);
                break;
            case DocumentTabMenuAction.MoveRight:
                MoveTabRight(tab);
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
            case DocumentTabMenuAction.Reopen:
                _ = ReopenTab(tab);
                break;
        }
    }

    private void CloseTab(DocumentTab tab)
    {
        var fileResource = tab.ViewModel.FileResource;
        ViewModel.OnCloseDocumentRequested(fileResource);
    }

    private void MoveTabLeft(DocumentTab tab)
    {
        if (tab.SectionIndex > 0)
        {
            SectionContainer.MoveTabToSection(tab, tab.SectionIndex - 1);
            UpdateAllTabDisplayNames();
            NotifyLayoutChanged();
        }
    }

    private void MoveTabRight(DocumentTab tab)
    {
        if (tab.SectionIndex < SectionContainer.SectionCount - 1)
        {
            SectionContainer.MoveTabToSection(tab, tab.SectionIndex + 1);
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
        var (section, _) = SectionContainer.FindDocumentTab(keepTab.ViewModel.FileResource);
        if (section == null)
        {
            return;
        }

        var tabsToClose = new List<ResourceKey>();

        // Only close other tabs within the same section
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
        var (section, _) = SectionContainer.FindDocumentTab(referenceTab.ViewModel.FileResource);
        if (section == null)
        {
            return;
        }

        var tabsToClose = new List<ResourceKey>();
        bool foundReference = false;

        // Close tabs to the right within the same section
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
        var (section, _) = SectionContainer.FindDocumentTab(referenceTab.ViewModel.FileResource);
        if (section == null)
        {
            return;
        }

        var tabsToClose = new List<ResourceKey>();

        // Close tabs to the left within the same section
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
        var (section, _) = SectionContainer.FindDocumentTab(referenceTab.ViewModel.FileResource);
        if (section == null)
        {
            return;
        }

        var tabsToClose = new List<ResourceKey>();

        // Only close tabs within the same section
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
        var fileResource = tab.ViewModel.FileResource;

        _commandService.Execute<ISelectResourceCommand>(command =>
        {
            command.Resource = fileResource;
            command.ShowExplorerPanel = true;
        });
    }

    private void CopyResourceKeyForTab(DocumentTab tab)
    {
        var fileResource = tab.ViewModel.FileResource;
        var resourceKey = fileResource.ToString();

        _commandService.Execute<ICopyTextToClipboardCommand>(command =>
        {
            command.Text = resourceKey;
            command.TransferMode = DataTransferMode.Copy;
        });
    }

    private void CopyFilePathForTab(DocumentTab tab)
    {
        var filePath = tab.ViewModel.FilePath;

        _commandService.Execute<ICopyTextToClipboardCommand>(command =>
        {
            command.Text = filePath;
            command.TransferMode = DataTransferMode.Copy;
        });
    }

    private void OpenFileExplorerForTab(DocumentTab tab)
    {
        var fileResource = tab.ViewModel.FileResource;

        _commandService.Execute<IOpenFileManagerCommand>(command =>
        {
            command.Resource = fileResource;
        });
    }

    private void OpenApplicationForTab(DocumentTab tab)
    {
        var fileResource = tab.ViewModel.FileResource;

        _commandService.Execute<IOpenApplicationCommand>(command =>
        {
            command.Resource = fileResource;
        });
    }

    private async Task ReopenTab(DocumentTab tab)
    {
        var fileResource = tab.ViewModel.FileResource;
        var extension = Path.GetExtension(fileResource.ToString()).ToLowerInvariant();
        var documentsService = ViewModel.GetDocumentsService();
        var editorRegistry = documentsService.DocumentEditorRegistry;
        var factories = editorRegistry.GetFactoriesForFileExtension(extension);

        var selectedEditorId = DocumentEditorId.Empty;

        if (factories.Count >= 2)
        {
            // Pre-select the current editor
            var currentEditorId = tab.ViewModel.EditorId;
            int defaultIndex = 0;
            for (int i = 0; i < factories.Count; i++)
            {
                if (factories[i].EditorId == currentEditorId)
                {
                    defaultIndex = i;
                    break;
                }
            }

            // Multiple editors available — show choice dialog
            var displayNames = factories.Select(f => f.DisplayName).ToList();
            var stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
            var dialogService = ServiceLocator.AcquireService<IDialogService>();

            var title = stringLocalizer.GetString("OpenWithDialog_Title");
            var message = stringLocalizer.GetString("OpenWithDialog_Message");
            var checkbox = new ChoiceDialogCheckbox(stringLocalizer.GetString("OpenWithDialog_UseAsDefault"));

            var choiceResult = await dialogService.ShowChoiceDialogAsync(title, message, displayNames, defaultIndex, checkbox);
            if (choiceResult.IsFailure)
            {
                return;
            }

            selectedEditorId = factories[choiceResult.Value.SelectedIndex].EditorId;

            if (choiceResult.Value.CheckboxChecked)
            {
                var workspaceSettings = ServiceLocator.AcquireService<IWorkspaceWrapper>()
                    .WorkspaceService.WorkspaceSettings;
                var preferenceKey = DocumentConstants.GetEditorPreferenceKey(extension);
                await workspaceSettings.SetPropertyAsync(preferenceKey, selectedEditorId.ToString());
            }
        }

        // Save editor state before closing
        string? editorState = null;
        if (tab.ViewModel.DocumentView is not null)
        {
            editorState = await tab.ViewModel.DocumentView.SaveEditorStateAsync();
        }

        // Close the document
        var closeResult = await tab.ViewModel.CloseDocument(forceClose: false);
        if (closeResult.IsFailure || !closeResult.Value)
        {
            return;
        }

        // Find and remove the tab from its section
        var (section, _) = SectionContainer.FindDocumentTab(fileResource);
        if (section is not null)
        {
            section.RemoveTab(tab);
        }

        NotifyLayoutChanged();

        // Reopen with the selected editor and restore editor state
        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
            command.ForceReload = true;
            command.EditorId = selectedEditorId;
            command.EditorStateJson = editorState;
        });
    }
}
