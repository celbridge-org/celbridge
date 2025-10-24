using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Messaging;
using Celbridge.Workspace;
using Windows.Foundation.Collections;

using Path = System.IO.Path;

namespace Celbridge.Documents.Views;

using IDocumentsLogger = Logging.ILogger<DocumentsPanel>;

public sealed partial class DocumentsPanel : UserControl, IDocumentsPanel
{
    private readonly IDocumentsLogger _logger;
    private readonly IMessengerService _messengerService;
    private readonly IResourceRegistry _resourceRegistry;

    private bool _isShuttingDown = false;

    public DocumentsPanelViewModel ViewModel { get; }

    public DocumentsPanel(
        IServiceProvider serviceProvider,
        IDocumentsLogger logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        InitializeComponent();

        _logger = logger;
        _messengerService = messengerService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

        ViewModel = serviceProvider.AcquireService<DocumentsPanelViewModel>();

        //
        // Set the data context
        //
        this.DataContext = ViewModel;

        UpdateTabstripEnds();

        // Listen for property changes on the ViewModel
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

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
        ViewModel.OnViewLoaded();
    }

    private void DocumentsPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.OnViewUnloaded();

        Loaded -= DocumentsPanel_Loaded;
        Unloaded -= DocumentsPanel_Unloaded;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.IsExplorerPanelVisible) ||
            e.PropertyName == nameof(ViewModel.IsInspectorPanelVisible))
        {
            UpdateTabstripEnds();
        }
    }

    private void UpdateTabstripEnds()
    {
        // When the left and right workspace panels are hidden, the panel visibility toggle buttons may overlap the
        // TabStrip at the top of the center panel. To fix this, we dynamically add an invisible TabStripHeader and
        // TabStripFooter which adjusts the position of the tabs so that they don't overlap the toggle buttons.

        if (ViewModel.IsExplorerPanelVisible)
        {
            TabView.TabStripHeader = null;
        }
        else
        {
            TabView.TabStripHeader = new Grid()
                .Width(96);
        }

        if (ViewModel.IsInspectorPanelVisible)
        {
            TabView.TabStripFooter = null;
        }
        else
        {
            TabView.TabStripFooter = new Grid()
                .Width(48);
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

    public class PathNode
    {
        public string entryString;
        public int groupIndex;

        // These fields are not used for any algorithmic reason but are very useful for tracing and debugging the algorithm.
        //  Also, if we do want to do any further work on this to make it more contextual this information will likely be what we need to use.
#if DEBUG
        public List<PathNode> nextNodes;
        public List<DocumentTab> tabIdentifiers;
#endif // DEBUG

        public PathNode(string entryString, int groupIndex, DocumentTab initialTabIndentifier)
        {
            this.entryString = entryString;
            this.groupIndex = groupIndex;
#if DEBUG
            nextNodes = new List<PathNode>();
            this.tabIdentifiers = new List<DocumentTab>() { initialTabIndentifier };
#endif // DEBUG
        }
    }

    public class PathWorkEntry
    {
        public int currentIndex;
        public string[] pathSegments;
        public List<string> displaySegments;
        public string finalDisplayString;
        public PathNode? currentNode;

        public PathWorkEntry(string path)
        {
            pathSegments = path.Split(Path.DirectorySeparatorChar);
            currentIndex = pathSegments.Length - 2;
            displaySegments = new List<string>();
            finalDisplayString = "";
        }
    }

    public async Task<Result> OpenDocument(ResourceKey fileResource, string filePath, bool forceReload)
    {
        string fileName = System.IO.Path.GetFileName(filePath);

        var collidedTabs = new Dictionary<DocumentTab, PathWorkEntry>();

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

                return Result.Ok();
            }
            else
            {
                // Check for alike filenames where we need to show a differentiation of paths.
                if (fileName == System.IO.Path.GetFileName(tab.ViewModel.FileResource))
                {
                    var otherFilePath = _resourceRegistry.GetResourcePath(tab.ViewModel.FileResource);
                    collidedTabs.Add(tab, new PathWorkEntry(otherFilePath));
                }
            }
        }


        //
        // Add a new DocumentTab to the TabView immediately.
        // This provides some early visual feedback that the document is loading.
        //

        var documentTab = new DocumentTab();
        documentTab.ViewModel.FileResource = fileResource;
        documentTab.ViewModel.FilePath = filePath;
        documentTab.ViewModel.DocumentName = fileResource.ResourceName; // fileResource.ResourceNameNoExtension;

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

        // Handle differentiation for alike filenames.
        if (collidedTabs.Count > 0)
        {
            collidedTabs.Add(documentTab, new PathWorkEntry(filePath));
            HandleCollidedTabs(ref collidedTabs);

            foreach (var tabInfo in collidedTabs)
            {
                // Update our display string for this tab.
                tabInfo.Key.ViewModel.DocumentName = tabInfo.Value.finalDisplayString;
            }
        }

        return Result.Ok();
    }

    public static void HandleCollidedTabs(ref Dictionary<DocumentTab, PathWorkEntry> collidedTabs)
    {
        // Make a list of tab identifiers to iterate to avoid iterate lock up on our instances (Thanks C#)
        var tabIdentifiers = new List<DocumentTab>();
        foreach (var pair in collidedTabs)
        {
            tabIdentifiers.Add(pair.Key);
        }

        int nextGroupIndex = 1;
        bool stillBusy = true;
        do
        {
            // Reset our busy flag.
            stillBusy = false;

            // Sort identifiers by group, ready to process.
            var GroupToIdentifiersDictionary = new Dictionary<int, List<DocumentTab>>();
            foreach (var identifier in tabIdentifiers)
            {
                PathWorkEntry workEntry = collidedTabs[identifier];
                if (workEntry.currentIndex < 0)
                {
                    continue;
                }

                stillBusy = true;       // If we reach here, - then there's still work to be done.

                int groupIndex = workEntry.currentNode != null ? workEntry.currentNode.groupIndex : 0;
                if (!GroupToIdentifiersDictionary.ContainsKey(groupIndex))
                {
                    GroupToIdentifiersDictionary.Add(groupIndex, new List<DocumentTab>() { identifier });
                }
                else
                {
                    GroupToIdentifiersDictionary[groupIndex].Add(identifier);
                }
            }

            // Run through identifiers in group blocks, with a clean node dictionary for each group.
            foreach (var pair in GroupToIdentifiersDictionary)
            {
                var NodeDictionary = new Dictionary<string, PathNode>();

                foreach (DocumentTab identifier in pair.Value)
                {
                    var workEntry = collidedTabs[identifier];
                    var segmentString = workEntry.pathSegments[workEntry.currentIndex];
                    if (NodeDictionary.ContainsKey(segmentString))
                    {
#if DEBUG
                        NodeDictionary[segmentString].tabIdentifiers.Add(identifier);
#endif // DEBUG
                    }
                    else
                    {
                        NodeDictionary.Add(segmentString, new PathNode(segmentString, nextGroupIndex++, identifier));
                    }

#if DEBUG
                    if (workEntry.currentNode != null)
                    {
                        if (!NodeDictionary[segmentString].nextNodes.Contains(workEntry.currentNode))
                        {
                            NodeDictionary[segmentString].nextNodes.Add(workEntry.currentNode);
                        }
                    }
#endif // DEBUG

                    workEntry.currentNode = NodeDictionary[segmentString];
                }

                // If we have only one node, then there is no deviation, so use '...', otherwise, use the segment.
                bool useSegment = NodeDictionary.Count > 1;
                foreach (var identifier in pair.Value)
                {
                    var workEntry = collidedTabs[identifier];
                    var segmentString = workEntry.pathSegments[workEntry.currentIndex];
                    if (useSegment)
                    {
                        workEntry.displaySegments.Add(segmentString);
                    }
                    else
                    {
                        if ((workEntry.displaySegments.Count > 0) && 
                            (workEntry.displaySegments[workEntry.displaySegments.Count - 1] != "..."))
                        {
                            workEntry.displaySegments.Add("...");
                        }
                    }
                    workEntry.currentIndex--;
                }
            }
        }
        while (stillBusy);

        // Render out to a string ready for output and tidy any leading '...' entries.
        foreach (var pair in collidedTabs)
        {
            PathWorkEntry workEntry = pair.Value;
            var displaySegments = workEntry.displaySegments;
            displaySegments.Reverse();
            string outputPath = "";
            foreach (var segment in displaySegments)
            {
                if (outputPath.Length > 0)
                {
                    outputPath += Path.DirectorySeparatorChar;
                }
                else
                {
                    if (segment == "...")
                    {
                        continue;
                    }
                }
                outputPath += segment;
            }

            // Add file name to the end of the path.
            outputPath += Path.DirectorySeparatorChar + workEntry.pathSegments[workEntry.pathSegments.Length - 1];

            workEntry.finalDisplayString = outputPath;
        }
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
                    TabView.TabItems.Remove(documentTab);
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

        return Result.Ok();
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
}
