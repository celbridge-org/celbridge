using Celbridge.Documents.Views;
using Celbridge.FileViewer.ViewModels;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.Workspace;

namespace Celbridge.FileViewer.Views;

public sealed partial class FileViewerDocumentView : WebView2DocumentView
{
    private readonly IResourceRegistry _resourceRegistry;

    public FileViewerDocumentViewModel ViewModel { get; }

    public override ResourceKey FileResource => ViewModel.FileResource;

    public FileViewerDocumentView(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper,
        IMessengerService messengerService,
        IWebViewFactory webViewFactory)
        : base(messengerService, webViewFactory)
    {
        ViewModel = serviceProvider.GetRequiredService<FileViewerDocumentViewModel>();

        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        this.InitializeComponent();

        // Set the container where the WebView will be placed
        WebViewContainer = FileWebViewContainer;
    }

    public override async Task<Result> SetFileResource(ResourceKey fileResource)
    {
        var filePath = _resourceRegistry.GetResourcePath(fileResource);

        if (_resourceRegistry.GetResource(fileResource).IsFailure)
        {
            return Result.Fail($"File resource does not exist in resource registry: {fileResource}");
        }

        if (!File.Exists(filePath))
        {
            return Result.Fail($"File resource does not exist on disk: {fileResource}");
        }

        ViewModel.FileResource = fileResource;
        ViewModel.FilePath = filePath;

        await Task.CompletedTask;

        return Result.Ok();
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
