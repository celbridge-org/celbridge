using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;

namespace Celbridge.Documents.Views;

public sealed partial class WebDocumentView : DocumentView
{
    public WebDocumentViewModel ViewModel { get; }

    private WebView2 _webView;

    public WebDocumentView()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<WebDocumentViewModel>();

        _webView = new WebView2()
            .Source(x => x.Bind(() => ViewModel.Source));

        // Fixes a visual bug where the WebView2 control would show a white background briefly when
        // switching between tabs. Similar issue described here: https://github.com/MicrosoftEdge/WebView2Feedback/issues/1412
        _webView.DefaultBackgroundColor = Colors.Transparent;

        //
        // Set the data context and control content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(_webView));
    }

    public override void SetFileResourceAndPath(ResourceKey fileResource, string filePath)
    {
        ViewModel.FileResource = fileResource;
        ViewModel.FilePath = filePath;
    }

    public override async Task<Result> LoadContent()
    {
        return await ViewModel.LoadContent();
    }
}
