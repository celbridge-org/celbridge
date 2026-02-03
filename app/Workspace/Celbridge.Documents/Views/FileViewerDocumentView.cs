using Celbridge.Documents.ViewModels;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Documents.Views;

public sealed partial class FileViewerDocumentView : DocumentView
{
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IMessengerService _messengerService;
    private readonly ILogger<FileViewerDocumentView> _logger;

    public FileViewerDocumentViewModel ViewModel { get; }

    private WebView2? _webView;

    public FileViewerDocumentView(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper,
        IMessengerService messengerService,
        ILogger<FileViewerDocumentView> logger)
    {
        ViewModel = serviceProvider.GetRequiredService<FileViewerDocumentViewModel>();

        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        _messengerService = messengerService;
        _logger = logger;

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
        if (_webView != null)
        {
            await _webView.EnsureCoreWebView2Async();

            // Inject centralized keyboard shortcut handler for F11 and other global shortcuts
            await WebView2Helper.InjectKeyboardShortcutHandlerAsync(_webView.CoreWebView2);

            // Listen for messages from the WebView (used for keyboard shortcut handling)
            _webView.WebMessageReceived -= WebView_WebMessageReceived;
            _webView.WebMessageReceived += WebView_WebMessageReceived;
        }

        return await ViewModel.LoadContent();
    }

    public override async Task PrepareToClose()
    {
        if (_webView != null)
        {
            _webView.GotFocus -= WebView_GotFocus;
            _webView.WebMessageReceived -= WebView_WebMessageReceived;
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

    private void WebView_WebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var message = args.TryGetWebMessageAsString();

        // Handle keyboard shortcuts via centralized helper
        WebView2Helper.HandleKeyboardShortcut(message);
    }
}
