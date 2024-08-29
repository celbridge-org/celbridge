using Celbridge.Documents.ViewModels;
using Celbridge.Logging;
using Celbridge.Resources;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Documents.Views;

public sealed partial class DocumentsPanel : UserControl, IDocumentsPanel
{
    private ILogger<DocumentsPanel> _logger;

    private TabView _tabView;

    public DocumentsPanelViewModel ViewModel { get; }

    private DocumentViewFactory _documentViewFactory = new();

    public DocumentsPanel()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        _logger = serviceProvider.GetRequiredService<ILogger<DocumentsPanel>>();

        ViewModel = serviceProvider.GetRequiredService<DocumentsPanelViewModel>();

        // Give the Documents Service a reference to this panel       
        var workspaceWrapper = serviceProvider.GetRequiredService<IWorkspaceWrapper>();
        var documentsService = workspaceWrapper.WorkspaceService.DocumentsService;
        Guard.IsNotNull(documentsService);
        documentsService.DocumentsPanel = this;

        // Create the tab view
        _tabView = new TabView()
            .IsAddTabButtonVisible(false)
            .TabWidthMode(TabViewWidthMode.SizeToContent)
            .VerticalAlignment(VerticalAlignment.Stretch);

        _tabView.TabCloseRequested += TabView_CloseRequested;

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

    private void TabView_CloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        var tab = args.Tab as DocumentTab;
        Guard.IsNotNull(tab);

        var fileResource = tab.ViewModel.ResourceKey;

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

    public async Task<Result> OpenDocument(ResourceKey fileResource, string filePath)
    {
        // Check if the file is already opened
        foreach (var tabItem in _tabView.TabItems)
        {
            var tab = tabItem as DocumentTab;
            Guard.IsNotNull(tab);

            if (fileResource == tab.ViewModel.ResourceKey)
            {
                //  Activate the existing tab instead of opening a new one
                _tabView.SelectedItem = tab;
                return Result.Ok();
            }
        }

        var createResult = await _documentViewFactory.CreateDocumentView(fileResource, filePath);
        if (createResult.IsFailure)
        {
            var failure = Result.Fail($"Failed to create document view for file resource: '{fileResource}'");
            failure.MergeErrors(createResult);
            return failure;
        }
        var documentView = createResult.Value;

        // Add a new DocumentTab to the TabView
        var documentTab = new DocumentTab();
        documentTab.ViewModel.ResourceKey = fileResource;
        documentTab.ViewModel.FilePath = filePath;
        documentTab.ViewModel.Name = fileResource.ResourceName;

        // Wait until the document control has loaded.
        bool loaded = false;
        documentView.Loaded += (sender, args) =>
        {
            loaded = true;
        };

        documentTab.Content = documentView;

        _tabView.TabItems.Add(documentTab);
        _tabView.SelectedItem = documentTab;

        while (!loaded)
        {
            await Task.Delay(25);
        }

        return Result.Ok();
    }

    public async Task<Result> CloseDocument(ResourceKey fileResource)
    {
        foreach (var tabItem in _tabView.TabItems)
        {
            var documentTab = tabItem as DocumentTab;
            Guard.IsNotNull(documentTab);

            if (fileResource == documentTab.ViewModel.ResourceKey)
            {
                var closeResult = await documentTab.ViewModel.CloseDocument();
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
        List<ResourceKey> failedSaves = new();

        foreach (var tabItem in _tabView.TabItems)
        {
            var documentTab = tabItem as DocumentTab;
            Guard.IsNotNull(documentTab);

            var documentView = documentTab.Content as IDocumentView;
            Guard.IsNotNull(documentView);

            if (documentView.IsDirty)
            {
                var updateResult = documentView.UpdateSaveTimer(deltaTime);
                Guard.IsTrue(updateResult.IsSuccess); // Should never fail

                var shouldSave = updateResult.Value;
                if (!shouldSave)
                {
                    continue;
                }

                var saveResult = await documentView.SaveDocument();
                if (saveResult.IsFailure)
                {
                    // Make a note of the failed save and continue saving other documents
                    failedSaves.Add(documentTab.ViewModel.ResourceKey);
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

        return Result.Ok();
    }
}
