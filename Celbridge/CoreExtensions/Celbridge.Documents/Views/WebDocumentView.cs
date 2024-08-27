using Celbridge.Documents.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Celbridge.Documents.Views;

public sealed partial class WebDocumentView : UserControl
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
}
