using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using Windows.Foundation.Collections;

namespace Celbridge.Documents.Views;

using IDocumentsLogger = Logging.ILogger<DocumentsPanel>;

public sealed partial class DocumentsPanel : UserControl, IDocumentsPanel
{
    private readonly IDocumentsLogger _logger;
    private readonly IMessengerService _messengerService;
    private readonly IResourceRegistry _resourceRegistry;
    private readonly ICommandService _commandService;
    private readonly ILayoutManager _layoutManager;

    private bool _isShuttingDown = false;

    public DocumentsPanelViewModel ViewModel { get; }

    public DocumentsPanel(
        IServiceProvider serviceProvider,
        IDocumentsLogger logger,
        IMessengerService messengerService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        ILayoutManager layoutManager)
    {
        InitializeComponent();

        _logger = logger;
        _messengerService = messengerService;
        _commandService = commandService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        _layoutManager = layoutManager;

        ViewModel = serviceProvider.AcquireService<DocumentsPanelViewModel>();

        //
        // Set the data context
        //
        this.DataContext = ViewModel;

        Loaded += DocumentsPanel_Loaded;
        Unloaded += DocumentsPanel_Unloaded;
    }

    private void TabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        ResourceKey documentResource = ResourceKey.Empty;

        var documentTab = TabView.SelectedItem as DocumentTab;
        if (documentTab is not null)
        {
            documentResource = documentTab.ViewModel.FileResource;
        }

        ViewModel.OnSelectedDocumentChanged(documentResource);
    }

    private void TabView_TabItemsChanged(TabView sender, IVectorChangedEventArgs args)
    {
        if (_isShuttingDown)
        {
            return;
        }

        var documentResources = GetOpenDocuments();
        ViewModel.OnOpenDocumentsChanged(documentResources);

        ToolTipService.SetToolTip(TabView, null);
    }

    private void TabView_CloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        var tab = args.Tab as DocumentTab;
        Guard.IsNotNull(tab);

        var fileResource = tab.ViewModel.FileResource;

        ViewModel.OnCloseDocumentRequested(fileResource);
    }

    private void DocumentsPanel_Loaded(object sender, RoutedEventArgs e)
    {
        // Listen for window mode changes to show/hide tab strip in Presenter mode
        _messengerService.Register<WindowModeChangedMessage>(this, OnWindowModeChanged);
        
        // Apply initial tab strip visibility based on current window mode
        UpdateTabStripVisibility(_layoutManager.WindowMode);
    }

    private void DocumentsPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnViewUnloaded();
        _messengerService.Unregister<WindowModeChangedMessage>(this);
    }

    private void OnWindowModeChanged(object recipient, WindowModeChangedMessage message)
    {
        UpdateTabStripVisibility(message.WindowMode);
    }

    private void UpdateTabStripVisibility(WindowMode windowMode)
    {
        // In Presenter mode, hide the tab strip to show only the document content
        // In all other modes, show the tab strip
        bool showTabStrip = windowMode != WindowMode.Presenter;
        
        // Find the TabStrip element within the TabView template and set its visibility
        // The TabView's tab strip is in a Grid row that we can collapse
        if (TabView.IsLoaded)
        {
            try
            {
                // From testing this, it appears we have to do both of these in order to hide/show the tab strip reliably

                // Use a style to control the tab strip visibility
                // This approach modifies the TabView's internal template
                var tabListView = VisualTreeHelperEx.FindDescendant<ListView>(TabView);
                if (tabListView != null)
                {
                    tabListView.Visibility = showTabStrip ? Visibility.Visible : Visibility.Collapsed;
                }

                // Also try to find and hide the tab strip container
                var tabStripContainer = VisualTreeHelperEx.FindDescendantByName(TabView, "TabContainerGrid");
                if (tabStripContainer is FrameworkElement container)
                {
                    container.Visibility = showTabStrip ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch
            {
                // Silently handle any template traversal errors
            }
        }
    }

    public List<ResourceKey> GetOpenDocuments()
    {
        var openDocuments = new List<ResourceKey>();
        foreach (var tabItem in TabView.TabItems)
        {
            var tab = tabItem as DocumentTab;
            Guard.IsNotNull(tab);

            var fileResource = tab.ViewModel.FileResource;
            Guard.IsFalse(openDocuments.Contains(fileResource));

            openDocuments.Add(fileResource);
        }

        return openDocuments;
    }

    public async Task<Result> OpenDocument(ResourceKey fileResource, string filePath, bool forceReload)
    {
        return await OpenDocument(fileResource, filePath, forceReload, string.Empty);
    }

    public async Task<Result> OpenDocument(ResourceKey fileResource, string filePath, bool forceReload, string location)
    {
        // Check if the file is already opened
        foreach (var tabItem in TabView.TabItems)
        {
            var tab = tabItem as DocumentTab;
            Guard.IsNotNull(tab);

            if (fileResource == tab.ViewModel.FileResource)
            {
                //  Activate the existing tab instead of opening a new one
                TabView.SelectedItem = tab;

                if (forceReload)
                {
                    var reloadResult = await tab.ViewModel.ReloadDocument();
                    if (reloadResult.IsFailure)
                    {
                        return Result.Fail($"Failed to reload document: {fileResource}")
                            .WithErrors(reloadResult);
                    }
                }

                // Navigate to location if specified
                if (!string.IsNullOrEmpty(location))
                {
                    await NavigateToLocation(fileResource, location);
                }

                return Result.Ok();
            }
        }

        //
        // Add a new DocumentTab to the TabView immediately.
        // This provides some early visual feedback that the document is loading.
        //

        var documentTab = new DocumentTab();
        documentTab.ViewModel.FileResource = fileResource;
        documentTab.ViewModel.FilePath = filePath;
        documentTab.ViewModel.DocumentName = fileResource.ResourceName;
        documentTab.ContextMenuActionRequested += OnDocumentTabContextMenuAction;

        // This triggers an update of the stored open documents, so documentTab.ViewModel.FileResource
        // must be populated at this point.
        TabView.TabItems.Add(documentTab);

        // Select the tab and make the content active
        TabView.SelectedItem = documentTab;

        int tabIndex = TabView.TabItems.Count - 1;

        var createResult = await ViewModel.CreateDocumentView(fileResource);
        if (createResult.IsFailure)
        {
            TabView.TabItems.RemoveAt(tabIndex);

            return Result.Fail($"Failed to create document view for file resource: '{fileResource}'")
                .WithErrors(createResult);
        }
        var documentView = createResult.Value;

        // Populate the tab content
        documentTab.ViewModel.DocumentView = documentView;
        documentTab.Content = documentView;

        // Select the tab and force the content to refresh
        TabView.SelectedItem = null;
        TabView.SelectedItem = documentTab;

        // Update all tab names to handle any filename ambiguity
        UpdateAllTabDisplayNames();

        // Navigate to location if specified
        if (!string.IsNullOrEmpty(location))
        {
            await NavigateToLocation(fileResource, location);
        }

        return Result.Ok();
    }

    public async Task<Result> NavigateToLocation(ResourceKey fileResource, string location)
    {
        foreach (var tabItem in TabView.TabItems)
        {
            var documentTab = tabItem as DocumentTab;
            Guard.IsNotNull(documentTab);

            if (fileResource == documentTab.ViewModel.FileResource)
            {
                var documentView = documentTab.Content as IDocumentView;
                if (documentView != null)
                {
                    return await documentView.NavigateToLocation(location);
                }
                return Result.Ok();
            }
        }

        return Result.Fail($"No opened document found for file resource: '{fileResource}'");
    }

    public async Task<Result> CloseDocument(ResourceKey fileResource, bool forceClose)
    {
        foreach (var tabItem in TabView.TabItems)
        {
            var documentTab = tabItem as DocumentTab;
            Guard.IsNotNull(documentTab);

            if (fileResource == documentTab.ViewModel.FileResource)
            {
                var closeResult = await documentTab.ViewModel.CloseDocument(forceClose);
                if (closeResult.IsFailure)
                {
                    return Result.Fail($"An error occured when closing the document for file resource: '{fileResource}'")
                        .WithErrors(closeResult);
                }

                var didClose = closeResult.Value;

                if (didClose)
                {
                    documentTab.ContextMenuActionRequested -= OnDocumentTabContextMenuAction;
                    TabView.TabItems.Remove(documentTab);

                    // Update all tab names since closing a tab may resolve filename ambiguity
                    UpdateAllTabDisplayNames();
                }

                return Result.Ok();
            }
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

        foreach (var tabItem in TabView.TabItems)
        {
            var documentTab = tabItem as DocumentTab;
            Guard.IsNotNull(documentTab);

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
                    var savedResource = documentTab.ViewModel.FileResource;
                    var message = new DocumentSaveRequestedMessage(savedResource);

                    _messengerService.Send(message);

                    savedCount++;
                }
            }
        }

        if (failedSaves.Count > 0)
        {
            return Result.Fail($"Failed to save the following documents: {string.Join(", ", failedSaves)}");
        }

        if (savedCount > 0)
        {
            _logger.LogDebug($"Saved {savedCount} modified documents");
        }

        ViewModel.UpdatePendingSaveCount(pendingSaveCount);

        return Result.Ok();
    }

    public Result SelectDocument(ResourceKey fileResource)
    {
        foreach (var tabItem in TabView.TabItems)
        {
            var documentTab = tabItem as DocumentTab;
            Guard.IsNotNull(documentTab);

            if (fileResource == documentTab.ViewModel.FileResource)
            {
                TabView.SelectedItem = documentTab;
                return Result.Ok();
            }
        }

        return Result.Fail($"No opened document found for file resource: '{fileResource}'");
    }

    public async Task<Result> ChangeDocumentResource(ResourceKey oldResource, DocumentViewType oldDocumentType, ResourceKey newResource, string newResourcePath, DocumentViewType newDocumentType)
    {
        // Find the document tab for the old resource
        DocumentTab? documentTab = null;
        int tabIndex = -1;
        for (int i = 0; i < TabView.TabItems.Count; i++)
        {
            object? tabItem = TabView.TabItems[i];
            var tab = tabItem as DocumentTab;
            Guard.IsNotNull(tab);

            if (oldResource == tab.ViewModel.FileResource)
            {
                documentTab = tab;
                tabIndex = i;
                break;
            }
        }

        if (documentTab is null)
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

            var selectedIndex = TabView.SelectedIndex;
            if (selectedIndex == tabIndex)
            {
                // This document is the selected tab.
                // Force a layout update to display its new contents.
                TabView.SelectedIndex = -1;
                TabView.SelectedIndex = selectedIndex;
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
    private void UpdateAllTabDisplayNames()
    {
        // Group tabs by their filename
        var tabsByFilename = new Dictionary<string, List<DocumentTab>>();
        foreach (var tabItem in TabView.TabItems)
        {
            var tab = tabItem as DocumentTab;
            Guard.IsNotNull(tab);

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

        // Close all open documents and clean up their WebView2 resources
        foreach (var tabItem in TabView.TabItems)
        {
            var documentTab = tabItem as DocumentTab;
            Guard.IsNotNull(documentTab);

            // Unsubscribe from context menu events
            documentTab.ContextMenuActionRequested -= OnDocumentTabContextMenuAction;

            var documentView = documentTab.Content as IDocumentView;
            if (documentView != null)
            {
                // Call PrepareToClose to clean up WebView2 resources
                // Note: We can't await here since Shutdown is synchronous, but the cleanup will happen asynchronously
                _ = documentView.PrepareToClose();
            }
        }

        TabView.TabItems.Clear();
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
                CloseAllTabs();
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
        }
    }

    private void CloseTab(DocumentTab tab)
    {
        var fileResource = tab.ViewModel.FileResource;
        ViewModel.OnCloseDocumentRequested(fileResource);
    }

    private void CloseOtherTabs(DocumentTab keepTab)
    {
        var tabsToClose = new List<DocumentTab>();
        foreach (var tabItem in TabView.TabItems)
        {
            var documentTab = tabItem as DocumentTab;
            if (documentTab != null && documentTab != keepTab)
            {
                tabsToClose.Add(documentTab);
            }
        }

        foreach (var tab in tabsToClose)
        {
            var fileResource = tab.ViewModel.FileResource;
            ViewModel.OnCloseDocumentRequested(fileResource);
        }
    }

    private void CloseOtherTabsRight(DocumentTab referenceTab)
    {
        var tabIndex = TabView.TabItems.IndexOf(referenceTab);
        if (tabIndex < 0) return;

        var tabsToClose = new List<DocumentTab>();
        for (int i = tabIndex + 1; i < TabView.TabItems.Count; i++)
        {
            var documentTab = TabView.TabItems[i] as DocumentTab;
            if (documentTab != null)
            {
                tabsToClose.Add(documentTab);
            }
        }

        foreach (var tab in tabsToClose)
        {
            var fileResource = tab.ViewModel.FileResource;
            ViewModel.OnCloseDocumentRequested(fileResource);
        }
    }

    private void CloseOtherTabsLeft(DocumentTab referenceTab)
    {
        var tabIndex = TabView.TabItems.IndexOf(referenceTab);
        if (tabIndex < 0) return;

        var tabsToClose = new List<DocumentTab>();
        for (int i = 0; i < tabIndex; i++)
        {
            var documentTab = TabView.TabItems[i] as DocumentTab;
            if (documentTab != null)
            {
                tabsToClose.Add(documentTab);
            }
        }

        foreach (var tab in tabsToClose)
        {
            var fileResource = tab.ViewModel.FileResource;
            ViewModel.OnCloseDocumentRequested(fileResource);
        }
    }

    private void CloseAllTabs()
    {
        var tabsToClose = new List<DocumentTab>();
        foreach (var tabItem in TabView.TabItems)
        {
            var documentTab = tabItem as DocumentTab;
            if (documentTab != null)
            {
                tabsToClose.Add(documentTab);
            }
        }

        foreach (var tab in tabsToClose)
        {
            var fileResource = tab.ViewModel.FileResource;
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
}
