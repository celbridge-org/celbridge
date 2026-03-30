using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.FileViewer.ViewModels;
using Celbridge.Messaging;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.WebView;

namespace Celbridge.FileViewer.Views;

public sealed partial class FileViewerDocumentView : WebViewDocumentView
{
    private readonly IMessengerService _messengerService;

    public FileViewerDocumentViewModel ViewModel { get; }

    protected override DocumentViewModel DocumentViewModel => ViewModel;

    public FileViewerDocumentView(
        IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IUserInterfaceService userInterfaceService,
        IWebViewFactory webViewFactory,
        IFeatureFlags featureFlags)
        : base(messengerService, webViewFactory, featureFlags)
    {
        _messengerService = messengerService;

        ViewModel = serviceProvider.GetRequiredService<FileViewerDocumentViewModel>();

        this.InitializeComponent();

        // Set the container where the WebView will be placed
        WebViewContainer = FileWebViewContainer;

        EnableThemeSyncing(userInterfaceService);

        Loaded += FileViewerDocumentView_Loaded;
    }

    public override async Task<Result> LoadContent()
    {
        return await ViewModel.LoadContent();
    }

    private async void FileViewerDocumentView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= FileViewerDocumentView_Loaded;

        await InitFileViewerAsync();
    }

    private async Task InitFileViewerAsync()
    {
        // Acquire WebView from factory and add to container
        await AcquireWebViewAsync();

        // Sync WebView2 color scheme with the app theme
        ApplyThemeToWebView();

        // Initialize the host
        InitializeHost();
        StartHostListener();

        // Navigate to the file
        if (!string.IsNullOrEmpty(ViewModel.Source))
        {
            WebView.CoreWebView2.Navigate(ViewModel.Source);
        }
    }

    public override async Task PrepareToClose()
    {
        _messengerService.UnregisterAll(this);

        Loaded -= FileViewerDocumentView_Loaded;

        ViewModel.Cleanup();

        await base.PrepareToClose();
    }
}
