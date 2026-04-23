using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Server;
using Celbridge.UserInterface;
using Celbridge.WebApp.ViewModels;
using Celbridge.WebView;
using Celbridge.WebView.Services;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebApp.Views;

/// <summary>
/// Hosts an arbitrary user URL from a .webapp document. Does not load a fixed
/// editor bundle and does not fit the contribution package model, so it inherits
/// DocumentView directly and owns its own WebView2 lifecycle.
/// </summary>
public sealed partial class WebAppDocumentView : DocumentView, IHostInput
{
    private readonly ILogger<WebAppDocumentView> _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IWebViewFactory _webViewFactory;
    private readonly IWebViewService _webViewService;

    private WebView2? _webView;
    private WebViewHostChannel? _hostChannel;
    private CelbridgeHost? _host;

    private bool _isFileServerReady;

    public WebAppDocumentViewModel ViewModel { get; }

    protected override DocumentViewModel DocumentViewModel => ViewModel;

    public WebAppDocumentView(
        IServiceProvider serviceProvider,
        ILogger<WebAppDocumentView> logger,
        ICommandService commandService,
        IMessengerService messengerService,
        IWebViewFactory webViewFactory,
        IFileServer projectFileServer,
        IWebViewService webViewService)
    {
        this.InitializeComponent();

        _logger = logger;
        _commandService = commandService;
        _messengerService = messengerService;
        _webViewFactory = webViewFactory;
        _webViewService = webViewService;
        _isFileServerReady = projectFileServer.IsReady;

        ViewModel = serviceProvider.GetRequiredService<WebAppDocumentViewModel>();

        Loaded += WebAppDocumentView_Loaded;

        _messengerService.Register<WebAppNavigateMessage>(this, OnWebAppNavigate);
        _messengerService.Register<WebAppRefreshMessage>(this, OnWebAppRefresh);
        _messengerService.Register<WebAppGoBackMessage>(this, OnWebAppGoBack);
        _messengerService.Register<WebAppGoForwardMessage>(this, OnWebAppGoForward);
        _messengerService.Register<ProjectFileServerReadyMessage>(this, OnProjectFileServerReady);
    }

    public void OnKeyboardShortcut(string key, bool ctrlKey, bool shiftKey, bool altKey)
    {
        var keyboardShortcutService = ServiceLocator.AcquireService<IKeyboardShortcutService>();
        keyboardShortcutService.HandleShortcut(key, ctrlKey, shiftKey, altKey);
    }

    private void OnProjectFileServerReady(object recipient, ProjectFileServerReadyMessage message)
    {
        _isFileServerReady = true;
        TryNavigate();
    }

    private void TryNavigate()
    {
        if (_webView?.CoreWebView2 is null || string.IsNullOrEmpty(ViewModel.SourceUrl))
        {
            return;
        }

        if (ViewModel.NeedsFileServer && !_isFileServerReady)
        {
            return;
        }

        var navigateUrl = ViewModel.NavigateUrl;
        if (!string.IsNullOrEmpty(navigateUrl))
        {
            _webView.CoreWebView2.Navigate(navigateUrl);
        }
    }

    private async void OnWebAppNavigate(object recipient, WebAppNavigateMessage message)
    {
        if (message.DocumentResource != ViewModel.FileResource)
        {
            return;
        }

        if (_webView is null)
        {
            return;
        }

        try
        {
            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.Navigate(message.Url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to navigate to URL: {message.Url}");
        }
    }

    private async void OnWebAppRefresh(object recipient, WebAppRefreshMessage message)
    {
        if (message.DocumentResource != ViewModel.FileResource)
        {
            return;
        }

        if (_webView is null)
        {
            return;
        }

        try
        {
            await _webView.EnsureCoreWebView2Async();

            await _webView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                CoreWebView2BrowsingDataKinds.CacheStorage | CoreWebView2BrowsingDataKinds.DiskCache);

            _webView.CoreWebView2.Reload();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh page");
        }
    }

    private async void OnWebAppGoBack(object recipient, WebAppGoBackMessage message)
    {
        if (message.DocumentResource != ViewModel.FileResource)
        {
            return;
        }

        if (_webView is null)
        {
            return;
        }

        try
        {
            await _webView.EnsureCoreWebView2Async();
            if (_webView.CoreWebView2.CanGoBack)
            {
                _webView.CoreWebView2.GoBack();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate back");
        }
    }

    private async void OnWebAppGoForward(object recipient, WebAppGoForwardMessage message)
    {
        if (message.DocumentResource != ViewModel.FileResource)
        {
            return;
        }

        if (_webView is null)
        {
            return;
        }

        try
        {
            await _webView.EnsureCoreWebView2Async();
            if (_webView.CoreWebView2.CanGoForward)
            {
                _webView.CoreWebView2.GoForward();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate forward");
        }
    }

    private async void WebAppDocumentView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= WebAppDocumentView_Loaded;

        // async void is required for a Loaded handler. Any exception in init
        // must be caught here so a faulty load cannot crash the process.
        try
        {
            _webView = await _webViewFactory.AcquireAsync();
            AppWebViewContainer.Children.Add(_webView);

            _webView.CoreWebView2.Settings.AreDevToolsEnabled = _webViewService.IsDevToolsFeatureEnabled();

            _hostChannel = new WebViewHostChannel(_webView.CoreWebView2);
            _host = new CelbridgeHost(_hostChannel);
            _host.AddLocalRpcTarget<IHostInput>(this);
            _host.StartListening();

            _webView.CoreWebView2.DownloadStarting -= CoreWebView2_DownloadStarting;
            _webView.CoreWebView2.DownloadStarting += CoreWebView2_DownloadStarting;

            _webView.CoreWebView2.NewWindowRequested -= WebView_NewWindowRequested;
            _webView.CoreWebView2.NewWindowRequested += WebView_NewWindowRequested;

            _webView.CoreWebView2.HistoryChanged -= CoreWebView2_HistoryChanged;
            _webView.CoreWebView2.HistoryChanged += CoreWebView2_HistoryChanged;

            _webView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
            _webView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

            TryNavigate();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize WebApp document view");
            TeardownWebViewState();
        }
    }

    /// <summary>
    /// Tears down the WebView, host channel, and associated event handlers. Safe
    /// to call multiple times and from partially initialized states. Used on
    /// orderly shutdown (PrepareToClose) and on failure recovery (Loaded catch).
    /// </summary>
    private void TeardownWebViewState()
    {
        if (_webView?.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.DownloadStarting -= CoreWebView2_DownloadStarting;
            _webView.CoreWebView2.NewWindowRequested -= WebView_NewWindowRequested;
            _webView.CoreWebView2.HistoryChanged -= CoreWebView2_HistoryChanged;
            _webView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
        }

        if (_webView is not null)
        {
            AppWebViewContainer.Children.Remove(_webView);
            _webView.Close();
            _webView = null;
        }

        _host?.Dispose();
        _hostChannel?.Detach();

        _host = null;
        _hostChannel = null;
    }

    private void CoreWebView2_HistoryChanged(object? sender, object e)
    {
        SendNavigationStateChanged();
    }

    private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        SendNavigationStateChanged();
    }

    private void SendNavigationStateChanged()
    {
        if (_webView is null)
        {
            return;
        }

        var coreWebView = _webView.CoreWebView2;

        var canRefresh = coreWebView is not null &&
                         !string.IsNullOrEmpty(coreWebView.Source) &&
                         coreWebView.Source != "about:blank";

        var currentUrl = coreWebView?.Source ?? string.Empty;

        var message = new WebAppNavigationStateChangedMessage(
            ViewModel.FileResource,
            coreWebView?.CanGoBack ?? false,
            coreWebView?.CanGoForward ?? false,
            canRefresh,
            currentUrl);

        _messengerService.Send(message);
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

        var resolveResult = ResourceRegistry.ResolveResourcePath(filename);
        if (resolveResult.IsFailure)
        {
            args.Cancel = true;
            return;
        }
        var requestedPath = resolveResult.Value;
        var getResult = PathHelper.GetUniquePath(requestedPath);
        if (getResult.IsFailure)
        {
            args.Cancel = true;
            return;
        }
        var savePath = getResult.Value;

        var getResourceResult = ResourceRegistry.GetResourceKey(savePath);
        if (getResourceResult.IsFailure)
        {
            args.Cancel = true;
            return;
        }
        var saveResourceKey = getResourceResult.Value;

        var extension = Path.GetExtension(filename);
        var tempPath = PathHelper.GetTemporaryFilePath("Downloads", extension);
        args.ResultFilePath = tempPath;

        args.DownloadOperation.StateChanged += (s, e) =>
        {
            if (s.State == CoreWebView2DownloadState.Completed)
            {
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

    public override async Task<Result> LoadContent()
    {
        var loadResult = await ViewModel.LoadContent();
        if (loadResult.IsFailure)
        {
            return loadResult;
        }

        if (_webView?.CoreWebView2 is not null)
        {
            TryNavigate();
        }

        return loadResult;
    }

    private void WebView_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;

        var url = args.Uri;
        if (!string.IsNullOrEmpty(url))
        {
            ViewModel.OpenBrowser(url);
        }
    }

    public override async Task PrepareToClose()
    {
        _messengerService.UnregisterAll(this);

        TeardownWebViewState();

        await base.PrepareToClose();
    }
}
