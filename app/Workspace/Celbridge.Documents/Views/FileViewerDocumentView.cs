using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.Documents.Views;

public sealed partial class FileViewerDocumentView : DocumentView
{
    private IResourceRegistry _resourceRegistry;
    private IMessengerService _messengerService;

    public FileViewerDocumentViewModel ViewModel { get; }

    private WebView2? _webView;

    public FileViewerDocumentView(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper,
        IMessengerService messengerService)
    {
        ViewModel = serviceProvider.GetRequiredService<FileViewerDocumentViewModel>();

        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        _messengerService = messengerService;

        _webView = new WebView2()
            .Source(x => x.Binding(() => ViewModel.Source));

        // Fixes a visual bug where the WebView2 control would show a white background briefly when
        // switching between tabs. Similar issue described here: https://github.com/MicrosoftEdge/WebView2Feedback/issues/1412
        _webView.DefaultBackgroundColor = Colors.Transparent;

        // Handle focus to set this document as active
        _webView.GotFocus += WebView_GotFocus;

        //
        // Set the data context and control content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(_webView));
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
        return await ViewModel.LoadContent();
    }

    public override async Task PrepareToClose()
    {
        if (_webView != null)
        {
            _webView.GotFocus -= WebView_GotFocus;
            _webView.Close();
            _webView = null;
        }

        await base.PrepareToClose();
    }

    private void WebView_GotFocus(object sender, RoutedEventArgs e)
    {
        // Set this document as the active document when the WebView2 receives focus
        var message = new DocumentViewFocusedMessage(ViewModel.FileResource);
        _messengerService.Send(message);
    }
}
