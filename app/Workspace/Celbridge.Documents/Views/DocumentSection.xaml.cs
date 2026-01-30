using Celbridge.UserInterface.Helpers;
using Microsoft.Extensions.Localization;
using Windows.Foundation.Collections;

namespace Celbridge.Documents.Views;

/// <summary>
/// A document section containing a TabView for managing document tabs.
/// Multiple sections can be displayed side-by-side in the DocumentSectionContainer.
/// </summary>
public sealed partial class DocumentSection : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;
    private bool _isShuttingDown = false;

    // Localized strings
    private string NoDocumentsOpenString => _stringLocalizer.GetString("DocumentSection_NoDocumentsOpen");

    /// <summary>
    /// The section index (0, 1, or 2) identifying this section's position.
    /// </summary>
    public int SectionIndex { get; set; }

    /// <summary>
    /// Event raised when the selected document changes in this section.
    /// </summary>
    public event Action<DocumentSection, ResourceKey>? SelectionChanged;

    /// <summary>
    /// Event raised when the open documents in this section change.
    /// </summary>
    public event Action<DocumentSection, List<ResourceKey>>? OpenDocumentsChanged;

    /// <summary>
    /// Event raised when a tab close is requested.
    /// </summary>
    public event Action<DocumentSection, ResourceKey>? CloseRequested;

    /// <summary>
    /// Event raised when a context menu action is requested on a document tab.
    /// </summary>
    public event Action<DocumentSection, DocumentTab, DocumentTabMenuAction>? ContextMenuActionRequested;

    public DocumentSection()
    {
        InitializeComponent();

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
    }

    /// <summary>
    /// Sets the content to display in the tab strip footer area.
    /// </summary>
    public void SetTabStripFooter(UIElement? content)
    {
        FooterPresenter.Content = content;
    }

    /// <summary>
    /// Gets the list of open documents in this section.
    /// </summary>
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

    /// <summary>
    /// Gets the currently selected document in this section, or ResourceKey.Empty if none.
    /// </summary>
    public ResourceKey GetSelectedDocument()
    {
        var documentTab = TabView.SelectedItem as DocumentTab;
        if (documentTab is not null)
        {
            return documentTab.ViewModel.FileResource;
        }
        return ResourceKey.Empty;
    }

    /// <summary>
    /// Checks if a document is open in this section.
    /// </summary>
    public bool ContainsDocument(ResourceKey fileResource)
    {
        foreach (var tabItem in TabView.TabItems)
        {
            var tab = tabItem as DocumentTab;
            Guard.IsNotNull(tab);

            if (fileResource == tab.ViewModel.FileResource)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Gets the DocumentTab for a given resource, or null if not found.
    /// </summary>
    public DocumentTab? GetDocumentTab(ResourceKey fileResource)
    {
        foreach (var tabItem in TabView.TabItems)
        {
            var tab = tabItem as DocumentTab;
            Guard.IsNotNull(tab);

            if (fileResource == tab.ViewModel.FileResource)
            {
                return tab;
            }
        }
        return null;
    }

    /// <summary>
    /// Gets or sets the number of currently visible sections. Used to determine which move options to show.
    /// </summary>
    public int VisibleSectionCount
    {
        get => _visibleSectionCount;
        set
        {
            _visibleSectionCount = value;
            // Update all tabs with the new count
            foreach (var tabItem in TabView.TabItems)
            {
                if (tabItem is DocumentTab tab)
                {
                    tab.VisibleSectionCount = value;
                }
            }
        }
    }
    private int _visibleSectionCount = 1;

    /// <summary>
    /// Adds a document tab to this section.
    /// </summary>
    public void AddTab(DocumentTab tab)
    {
        tab.SectionIndex = SectionIndex;
        // Set from cached value - stays in sync via VisibleSectionCount property setter
        tab.VisibleSectionCount = VisibleSectionCount;
        tab.ContextMenuActionRequested += OnDocumentTabContextMenuAction;
        TabView.TabItems.Add(tab);
        UpdateEmptyPlaceholderVisibility();
    }

    /// <summary>
    /// Removes a document tab from this section.
    /// </summary>
    public void RemoveTab(DocumentTab tab)
    {
        tab.ContextMenuActionRequested -= OnDocumentTabContextMenuAction;
        TabView.TabItems.Remove(tab);
        UpdateEmptyPlaceholderVisibility();
    }

    /// <summary>
    /// Selects a document tab in this section.
    /// </summary>
    public void SelectTab(DocumentTab tab)
    {
        TabView.SelectedItem = tab;
    }

    /// <summary>
    /// Clears the selection in this section.
    /// </summary>
    public void ClearSelection()
    {
        TabView.SelectedItem = null;
    }

    /// <summary>
    /// Gets the number of tabs in this section.
    /// </summary>
    public int TabCount => TabView.TabItems.Count;

    /// <summary>
    /// Gets all document tabs in this section.
    /// </summary>
    public IEnumerable<DocumentTab> GetAllTabs()
    {
        foreach (var tabItem in TabView.TabItems)
        {
            var tab = tabItem as DocumentTab;
            Guard.IsNotNull(tab);
            yield return tab;
        }
    }

    /// <summary>
    /// Forces a refresh of the selected tab's content.
    /// </summary>
    public void RefreshSelectedTab()
    {
        var selectedItem = TabView.SelectedItem;
        TabView.SelectedItem = null;
        TabView.SelectedItem = selectedItem;
    }

    /// <summary>
    /// Updates the tab strip visibility for presenter mode.
    /// </summary>
    public void UpdateTabStripVisibility(bool showTabStrip)
    {
        if (TabView.IsLoaded)
        {
            try
            {
                var tabListView = VisualTreeHelperEx.FindDescendant<ListView>(TabView);
                if (tabListView != null)
                {
                    tabListView.Visibility = showTabStrip ? Visibility.Visible : Visibility.Collapsed;
                }

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

    /// <summary>
    /// Prepares this section for shutdown.
    /// </summary>
    public void Shutdown()
    {
        _isShuttingDown = true;

        foreach (var tabItem in TabView.TabItems)
        {
            var documentTab = tabItem as DocumentTab;
            Guard.IsNotNull(documentTab);

            documentTab.ContextMenuActionRequested -= OnDocumentTabContextMenuAction;

            var documentView = documentTab.Content as IDocumentView;
            if (documentView != null)
            {
                _ = documentView.PrepareToClose();
            }
        }

        TabView.TabItems.Clear();
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

        SelectionChanged?.Invoke(this, documentResource);
    }

    private void TabView_TabItemsChanged(TabView sender, IVectorChangedEventArgs args)
    {
        if (_isShuttingDown)
        {
            return;
        }

        var documentResources = GetOpenDocuments();
        OpenDocumentsChanged?.Invoke(this, documentResources);

        ToolTipService.SetToolTip(TabView, null);
        UpdateEmptyPlaceholderVisibility();
    }

    private void TabView_CloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        var tab = args.Tab as DocumentTab;
        Guard.IsNotNull(tab);

        var fileResource = tab.ViewModel.FileResource;

        CloseRequested?.Invoke(this, fileResource);
    }

    private void OnDocumentTabContextMenuAction(DocumentTab tab, DocumentTabMenuAction action)
    {
        ContextMenuActionRequested?.Invoke(this, tab, action);
    }

    private void UpdateEmptyPlaceholderVisibility()
    {
        EmptyPlaceholder.Visibility = TabView.TabItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TabView.Visibility = TabView.TabItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
