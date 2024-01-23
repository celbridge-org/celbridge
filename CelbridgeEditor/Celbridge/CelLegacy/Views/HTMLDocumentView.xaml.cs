using Microsoft.Web.WebView2.Core;

namespace CelLegacy.Views;

public partial class HTMLDocumentView : TabViewItem, IDocumentView
{
    public HTMLDocumentViewModel ViewModel { get; }

    public HTMLDocumentView()
    {
        this.InitializeComponent();

        ViewModel = LegacyServiceProvider.Services!.GetRequiredService<HTMLDocumentViewModel>();

        ViewModel.RefreshRequested += ViewModel_RefreshRequested;
    }

    private void ViewModel_RefreshRequested()
    {
        if (HTMLView.CoreWebView2 != null)
        {
            HTMLView.CoreWebView2.Reload();
        }
    }

    public IDocument Document
    {
        get => ViewModel.Document;
        set => ViewModel.Document = value;
    }

    public void CloseDocument()
    {
        HTMLView.Source = new Uri("https://google.com");
        ViewModel.CloseDocumentCommand.Execute(null);
    }

    public async Task<Result> LoadDocumentAsync()
    {
        return await ViewModel.LoadDocumentAsync();
    }

    private void OnNavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        // Log.Information($"Navigation starting: {args.Uri}");
    }

    private void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        // Log.Information($"Navigation completed: {args}");
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        async Task LoadCodeEditor()
        {
            await HTMLView.EnsureCoreWebView2Async();
            HTMLView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "WebAssets",
                "Web",
                CoreWebView2HostResourceAccessKind.Allow);
            //HTMLView.CoreWebView2.Navigate("http://localhost:5120/");
            HTMLView.CoreWebView2.Navigate("http://WebAssets/index.html");

            // Todo: it's displaying a Blazor widget but then complains it can't find the content?
            // It's not a http thing, I tested that.
            // Try publishing a debug version and using that?
            // Don't spend all day on it anyway, we can put a version online and just download it every time if we have to.
        }

        _ = LoadCodeEditor();

    }
}
