using System.ComponentModel;
using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.UserInterface;
using Celbridge.Utilities;
using Celbridge.Workspace;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Documents.Views;

public sealed partial class WebAppDocumentView : DocumentView
{
    private ILogger<WebAppDocumentView> _logger;
    private ICommandService _commandService;
    private IUtilityService _utilityService;
    private IResourceRegistry _resourceRegistry;

    public WebAppDocumentViewModel ViewModel { get; }

    private WebView2? _webView;

    public WebAppDocumentView(
        IServiceProvider serviceProvider,
        ILogger<WebAppDocumentView> logger,
        ICommandService commandService,
        IUtilityService utilityService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _commandService = commandService;
        _utilityService = utilityService;

        ViewModel = serviceProvider.GetRequiredService<WebAppDocumentViewModel>();

        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        _webView = new WebView2();

        // Fixes a visual bug where the WebView2 control would show a white background briefly when
        // switching between tabs. Similar issue described here: https://github.com/MicrosoftEdge/WebView2Feedback/issues/1412
        _webView.DefaultBackgroundColor = Colors.Transparent;

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        //
        // Set the data context and control content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(_webView));
    }

    private async void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.SourceUrl))
        {
            try
            {
                if (_webView == null)
                {
                    return;
                }
                
                await _webView.EnsureCoreWebView2Async();
                _webView.CoreWebView2.Navigate(ViewModel.SourceUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to open URL: {ViewModel.SourceUrl}");
            }
        }
    }

    private void CoreWebView2_DownloadStarting(CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs args)
    {
        var downloadPath = args.ResultFilePath;
        if (string.IsNullOrEmpty(downloadPath))
        {
            args.Cancel = true;
            return;
        }
        
        var filename = Path.GetFileName(downloadPath);

        //
        // Map the download path to a unique path in the project folder 
        //
        var requestedPath = _resourceRegistry.GetResourcePath(filename);
        var getResult = _utilityService.GetUniquePath(requestedPath);
        if (getResult.IsFailure)
        {
            // Don't allow the download to proceed if we can't generate a unique path
            args.Cancel = true;
            return;
        }
        var savePath = getResult.Value;

        //
        // Get the resource key for the save path
        //
        var getResourceResult = _resourceRegistry.GetResourceKey(savePath);
        if (getResourceResult.IsFailure)
        {
            args.Cancel = true;
            return;
        }
        var saveResourceKey = getResourceResult.Value;

        //
        // Redirect download to a temporary path
        //
        var extension = Path.GetExtension(filename);
        var tempPath = _utilityService.GetTemporaryFilePath("Downloads", extension);
        args.ResultFilePath = tempPath;

        //
        // Handle download state changes
        //
        args.DownloadOperation.StateChanged += (s, e) =>
        {
            if (s.State == CoreWebView2DownloadState.Completed)
            {
                // Move the file to the requested path, with undo support.
                // _logger.LogInformation($"Downloaded: {requestedPath}");
                _commandService.Execute<IAddResourceCommand>(command =>
                {
                    command.ResourceType = ResourceType.File;
                    command.SourcePath = tempPath;
                    command.DestResource = saveResourceKey;
                });
            }
            else if (s.State == CoreWebView2DownloadState.Interrupted)
            {
                File.Delete(tempPath);
            }
        };
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
        // Be aware that this method can be called multiple times if the document is reloaded as a result of
        // the user changing the URL in the inspector.

        if (_webView == null)
        {
            return Result.Fail("WebView2 control is not initialized");
        }

        await _webView.EnsureCoreWebView2Async();

        // Ensure we only register once for these events
        _webView.CoreWebView2.DownloadStarting -= CoreWebView2_DownloadStarting;
        _webView.CoreWebView2.DownloadStarting += CoreWebView2_DownloadStarting;

        _webView.CoreWebView2.NewWindowRequested -= WebView_NewWindowRequested;
        _webView.CoreWebView2.NewWindowRequested += WebView_NewWindowRequested;

        // Listen for messages from the WebView (used for keyboard shortcut handling)
        _webView.WebMessageReceived -= WebView_WebMessageReceived;
        _webView.WebMessageReceived += WebView_WebMessageReceived;

        // Inject JavaScript to handle F11 key for full screen toggle.
        await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
            (function() {
                window.addEventListener('keydown', function(event) {
                    if (event.key === 'F11') {
                        event.preventDefault();
                        if (window.chrome && window.chrome.webview) {
                            window.chrome.webview.postMessage('toggle_layout');
                        }
                    }
                });
            })();
        ");

        return await ViewModel.LoadContent();
    }

    private void WebView_WebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var message = args.TryGetWebMessageAsString();
        if (message == "toggle_layout")
        {
            _commandService.Execute<ISetLayoutCommand>(command =>
            {
                command.Transition = LayoutTransition.ToggleLayout;
            });
        }
    }

    private void WebView_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        // Prevent the new window from being created
        args.Handled = true;

        // Open the url in the default system browser
        var url = args.Uri;
        if (!string.IsNullOrEmpty(url))
        {
            ViewModel.OpenBrowser(url);
        }
    }

    public override async Task PrepareToClose()
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;

        if (_webView != null)
        {
            _webView.WebMessageReceived -= WebView_WebMessageReceived;
            
            if (_webView.CoreWebView2 != null)
            {
                _webView.CoreWebView2.DownloadStarting -= CoreWebView2_DownloadStarting;
                _webView.CoreWebView2.NewWindowRequested -= WebView_NewWindowRequested;
            }

            _webView.Close();
            _webView = null;
        }

        await base.PrepareToClose();
    }
}
