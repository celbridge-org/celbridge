using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;

namespace Celbridge.Documents.Views;

public sealed partial class WebDocumentView : DocumentView
{
    public WebDocumentViewModel ViewModel { get; }

    public WebDocumentView()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<WebDocumentViewModel>();

        var webView = new WebView2()
            .Source(x => x.Bind(() => ViewModel.Source));

        //
        // Set the data context and control content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(webView));
    }

    public override void UpdateDocumentResource(ResourceKey fileResource, string filePath)
    {
        ViewModel.FileResource = fileResource;
        ViewModel.FilePath = filePath;
    }
}
