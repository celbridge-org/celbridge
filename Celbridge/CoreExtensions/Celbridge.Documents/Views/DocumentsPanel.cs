using Celbridge.Documents.Services;
using Celbridge.Documents.ViewModels;
using Celbridge.Resources;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Documents.Views;

public sealed partial class DocumentsPanel : UserControl, IDocumentsView
{
    private TabView _tabView;

    public DocumentsPanelViewModel ViewModel { get; }

    public DocumentsPanel()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<DocumentsPanelViewModel>();

        ViewModel.DocumentsView = this;

        _tabView = new TabView()
            .IsAddTabButtonVisible(false)
            .TabWidthMode(TabViewWidthMode.SizeToContent)
            //.TabCloseRequested = "DocumentTabView_TabCloseRequested"
            .VerticalAlignment(VerticalAlignment.Stretch);

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

    public async Task<Result> OpenFileDocument(ResourceKey fileResource, string filePath)
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

        // Add a new DocumentTab to the TabView
        var documentTab = new DocumentTab();
        documentTab.ViewModel.ResourceKey = fileResource;
        documentTab.ViewModel.FilePath = filePath;
        documentTab.ViewModel.Name = fileResource.ResourceName;

        var documentView = new WebDocumentView();

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
}
