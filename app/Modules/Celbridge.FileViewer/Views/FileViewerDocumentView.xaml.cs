using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.FileViewer.ViewModels;
using Celbridge.Messaging;
using Celbridge.WebView;

namespace Celbridge.FileViewer.Views;

public sealed partial class FileViewerDocumentView : WebViewDocumentView
{
    public FileViewerDocumentViewModel ViewModel { get; }

    protected override DocumentViewModel DocumentViewModel => ViewModel;

    public FileViewerDocumentView(
        IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IWebViewFactory webViewFactory)
        : base(messengerService, webViewFactory)
    {
        ViewModel = serviceProvider.GetRequiredService<FileViewerDocumentViewModel>();

        this.InitializeComponent();

        // Set the container where the WebView will be placed
        WebViewContainer = FileWebViewContainer;
    }

    public override async Task<Result> LoadContent()
    {
        // Acquire WebView from factory and add to container
        await AcquireWebViewAsync();

        // Initialize the host
        InitializeHost();
        StartHostListener();

        // Load content and navigate to the file
        var loadResult = await ViewModel.LoadContent();
        if (loadResult.IsSuccess && !string.IsNullOrEmpty(ViewModel.Source))
        {
            WebView.CoreWebView2.Navigate(ViewModel.Source);
        }

        return loadResult;
    }
}
