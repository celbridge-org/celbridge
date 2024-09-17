using Celbridge.Documents.ViewModels;
using Celbridge.Logging;
using CommunityToolkit.Diagnostics;
using Windows.Foundation.Collections;

namespace Celbridge.Documents.Views;

public sealed partial class DocumentsPanel : UserControl, IDocumentsPanel
{
    private ILogger<DocumentsPanel> _logger;

    private TabView _tabView;

    public DocumentsPanelViewModel ViewModel { get; }

    public DocumentsPanel()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        _logger = serviceProvider.GetRequiredService<ILogger<DocumentsPanel>>();

        ViewModel = serviceProvider.GetRequiredService<DocumentsPanelViewModel>();

        // Create the tab view
        _tabView = new TabView()
            .IsAddTabButtonVisible(false)
            .TabWidthMode(TabViewWidthMode.SizeToContent)
            .VerticalAlignment(VerticalAlignment.Stretch);

        _tabView.TabCloseRequested += TabView_CloseRequested;
        _tabView.SelectionChanged += TabView_SelectionChanged;
        _tabView.TabItemsChanged += TabView_TabItemsChanged;

        //
        // Set the data context and page content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(_tabView));

        UpdateTabstripEnds();

        // Listen for property changes on the ViewModel
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        Loaded += DocumentsPanel_Loaded;
        Unloaded += DocumentsPanel_Unloaded;
    }

    private void TabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ResourceKey documentResource = ResourceKey.Empty;

        var documentTab = _tabView.SelectedItem as DocumentTab;
        if (documentTab is not null)
        {
            documentResource = documentTab.ViewModel.FileResource;
        }

        ViewModel.OnSelectedDocumentChanged(documentResource);
    }

    private void TabView_TabItemsChanged(TabView sender, IVectorChangedEventArgs args)
    {
        var documentResources = GetOpenDocuments();
        ViewModel.OnOpenDocumentsChanged(documentResources);
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
        if (e.PropertyName == nameof(ViewModel.IsLeftPanelVisible) ||
            e.PropertyName == nameof(ViewModel.IsRightPanelVisible))
        {
            UpdateTabstripEnds();
        }
    }

    private void UpdateTabstripEnds()
    {
        // When the left and right workspace panels are hidden, the panel visibility toggle buttons may overlap the
        // TabStrip at the top of the center panel. To fix this, we dynamically add an invisible TabStripHeader and
        // TabStripFooter which adjusts the position of the tabs so that they don't overlap the toggle buttons.

        if (ViewModel.IsLeftPanelVisible)
        {
            _tabView.TabStripHeader = null;
        }
        else
        {
            _tabView.TabStripHeader = new Grid()
                .Width(96);
        }

        if (ViewModel.IsRightPanelVisible)
        {
            _tabView.TabStripFooter = null;
        }
        else
        {
            _tabView.TabStripFooter = new Grid()
                .Width(48);
        }
    }

    public List<ResourceKey> GetOpenDocuments()
    {
        var openDocuments = new List<ResourceKey>();
        foreach (var tabItem in _tabView.TabItems)
        {
            var tab = tabItem as DocumentTab;
            Guard.IsNotNull(tab);

            var fileResource = tab.ViewModel.FileResource;
            Guard.IsFalse(openDocuments.Contains(fileResource));

            openDocuments.Add(fileResource);
        }

        return openDocuments;
    }

    public async Task<Result> OpenDocument(ResourceKey fileResource, string filePath)
    {
        // Check if the file is already opened
        foreach (var tabItem in _tabView.TabItems)
        {
            var tab = tabItem as DocumentTab;
            Guard.IsNotNull(tab);

            if (fileResource == tab.ViewModel.FileResource)
            {
                //  Activate the existing tab instead of opening a new one
                _tabView.SelectedItem = tab;
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

        // This triggers an update of the stored open documents, so documentTab.ViewModel.FileResource
        // must be populated at this point.
        _tabView.TabItems.Add(documentTab);

        // Select the tab and make the content active
        _tabView.SelectedItem = documentTab;

        int tabIndex = _tabView.TabItems.Count - 1;

        var createResult = await ViewModel.CreateDocumentView(fileResource, filePath);
        if (createResult.IsFailure)
        {
            _tabView.TabItems.RemoveAt(tabIndex);

            var failure = Result.Fail($"Failed to create document view for file resource: '{fileResource}'");
            failure.MergeErrors(createResult);
            return failure;
        }
        var documentView = createResult.Value;

        // Populate the tab content
        documentTab.ViewModel.DocumentView = documentView;
        documentTab.Content = documentView;

        // Select the tab and force the content to refresh
        _tabView.SelectedItem = null;
        _tabView.SelectedItem = documentTab;

        return Result.Ok();
    }

    public async Task<Result> CloseDocument(ResourceKey fileResource, bool forceClose)
    {
        foreach (var tabItem in _tabView.TabItems)
        {
            var documentTab = tabItem as DocumentTab;
            Guard.IsNotNull(documentTab);

            if (fileResource == documentTab.ViewModel.FileResource)
            {
                var closeResult = await documentTab.ViewModel.CloseDocument(forceClose);
                if (closeResult.IsFailure)
                {
                    var failure = Result.Fail($"An error occured when closing the document for file resource: '{fileResource}'");
                    failure.MergeErrors(closeResult);
                    return failure;
                }

                var didClose = closeResult.Value;

                if (didClose)
                {
                    _tabView.TabItems.Remove(documentTab);
                }

                return Result.Ok();
            }
        }

        return Result.Fail($"No opened document found for file resource: '{fileResource}'");
    }

    public async Task<Result> SaveModifiedDocuments(double deltaTime)
    {
        int savedCount = 0;
        int pendingSaveCount = 0;
        List<ResourceKey> failedSaves = new();

        foreach (var tabItem in _tabView.TabItems)
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
            _logger.LogInformation($"Saved {savedCount} modified documents");
        }

        ViewModel.UpdatePendingSaveCount(pendingSaveCount);

        return Result.Ok();
    }

    public Result SelectDocument(ResourceKey fileResource)
    {
        foreach (var tabItem in _tabView.TabItems)
        {
            var documentTab = tabItem as DocumentTab;
            Guard.IsNotNull(documentTab);

            if (fileResource == documentTab.ViewModel.FileResource)
            {
                _tabView.SelectedItem = documentTab;
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
        for (int i = 0; i < _tabView.TabItems.Count; i++)
        {
            object? tabItem = _tabView.TabItems[i];
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
            var setResult = oldDocumentView.SetFileResource(newResource);
            if (setResult.IsFailure)
            {
                var failure = Result.Fail($"Failed to set file resource for document: '{newResource}'");
                failure.MergeErrors(setResult);
                return failure;
            }
        }
        else
        {
            var createResult = await ViewModel.CreateDocumentView(newResource, newResourcePath);
            if (createResult.IsFailure)
            {
                var failure = Result.Fail($"Failed to create document view for resource: '{newResource}'");
                failure.MergeErrors(createResult);
                return failure;
            }
            var newDocumentView = createResult.Value;

            // Clean up the old DocumentView state
            oldDocumentView.PrepareToClose();

            // Populate the tab content
            documentTab.ViewModel.DocumentView = newDocumentView;
            documentTab.Content = newDocumentView;

            // At this point there should be no remaining references to oldDocumentView, so it should go
            // out of scope and eventually be cleaned up by GC.

            var selectedIndex = _tabView.SelectedIndex;
            if (selectedIndex == tabIndex)
            {
                // This document is the selected tab.
                // Force a layout update to display its new contents.
                _tabView.SelectedIndex = -1;
                _tabView.SelectedIndex = selectedIndex;
            }
        }

        documentTab.ViewModel.FileResource = newResource;
        documentTab.ViewModel.DocumentName = newResource.ResourceName;
        documentTab.ViewModel.FilePath = newResourcePath;

        return Result.Ok();
    }
}
