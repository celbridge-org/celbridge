namespace CelLegacy.Views;

public interface IDocumentsPanelView
{
    bool TryFocusDocumentTab(IDocument document);
    Result OpenDocumentTab(TabViewItem tabItem);
    void CloseDocumentTab(IDocument document);
}

public sealed partial class DocumentsPanel : UserControl, IDocumentsPanelView
{
    public DocumentsViewModel ViewModel {get; set; }

    public DocumentsPanel()
    {
        this.InitializeComponent();
        ViewModel = LegacyServiceProvider.Services!.GetRequiredService<DocumentsViewModel>();
        ViewModel.DocumentsPanelView = this;
    }

    public bool TryFocusDocumentTab(IDocument document)
    {
        foreach (var tabViewitem in DocumentTabView.TabItems)
        {
            var documentView = tabViewitem as IDocumentView;
            Guard.IsNotNull(documentView);

            if (documentView.Document == document)
            {
                DocumentTabView.SelectedItem = tabViewitem;
                return true;
            }
        }
        return false;
    }

    public Result OpenDocumentTab(TabViewItem tabItem)
    {
        Guard.IsNotNull(tabItem);

        try
        {
            DocumentTabView.TabItems.Add(tabItem);
            DocumentTabView.SelectedItem = tabItem;
        }
        catch (Exception ex)
        {
            return new ErrorResult($"Failed to open document tab. {ex.Message}");
        }

        return new SuccessResult();
    }

    public void CloseDocumentTab(IDocument document)
    {
        TabViewItem? tabViewItem = null;
        foreach (var tab in DocumentTabView.TabItems)
        {
            var documentView = tab as IDocumentView;
            Guard.IsNotNull(documentView);

            if (documentView.Document == document)
            {
                tabViewItem = tab as TabViewItem;
                break;
            }
        }

        Guard.IsNotNull(tabViewItem);
        DocumentTabView.TabItems.Remove(tabViewItem);
    }

    private void DocumentTabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        var tabViewItem = args.Item as TabViewItem;
        Guard.IsNotNull(tabViewItem);

        var documentView = tabViewItem as IDocumentView;
        Guard.IsNotNull(documentView);

        documentView.CloseDocument();
    }
}
