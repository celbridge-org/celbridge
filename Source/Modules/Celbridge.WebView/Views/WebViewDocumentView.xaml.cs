using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Documents;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.WebHost;
using Celbridge.WebHost.Services;
using Celbridge.WebView.Services;
using Celbridge.WebView.ViewModels;
using Microsoft.Extensions.Localization;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebView.Views;

/// <summary>
/// Hosts an arbitrary user URL from a .webview document, or a project-served HTML
/// page from a .html / .htm document. The role is selected per-instance via Options
/// before LoadContent runs; the two paths share a single WebView2 lifecycle and
/// differ only in URL source and navigation policy.
/// </summary>
public sealed partial class WebViewDocumentView : DocumentView, IHostInput
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WebViewDocumentView> _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IWebViewFactory _webViewFactory;
    private readonly IWebViewService _webViewService;

    private WebView2? _webView;
    private WebViewHostChannel? _hostChannel;
    private CelbridgeHost? _host;
    private IWebViewNavigationPolicy? _navigationPolicy;

    private static readonly WebViewDocumentOptions DefaultOptions = new(
        WebViewDocumentRole.ExternalUrl,
        InterceptTopFrameNavigation: false);

    /// <summary>
    /// Per-instance options supplied by the editor factory. Defaults to the .webview
    /// external-URL behaviour; the HTML viewer factory overrides this before LoadContent
    /// runs so the view's first init applies HtmlViewer options from the start.
    /// </summary>
    internal WebViewDocumentOptions Options { get; set; } = DefaultOptions;

    public WebViewDocumentViewModel ViewModel { get; }

    protected override DocumentViewModel DocumentViewModel => ViewModel;

    public WebViewDocumentView(
        IServiceProvider serviceProvider,
        ILogger<WebViewDocumentView> logger,
        ICommandService commandService,
        IMessengerService messengerService,
        IWebViewFactory webViewFactory,
        IWebViewService webViewService)
    {
        this.InitializeComponent();

        _serviceProvider = serviceProvider;
        _logger = logger;
        _commandService = commandService;
        _messengerService = messengerService;
        _webViewFactory = webViewFactory;
        _webViewService = webViewService;

        ViewModel = serviceProvider.GetRequiredService<WebViewDocumentViewModel>();

        Loaded += WebViewDocumentView_Loaded;

        _messengerService.Register<WebViewNavigateMessage>(this, OnWebViewNavigate);
        _messengerService.Register<WebViewRefreshMessage>(this, OnWebViewRefresh);
        _messengerService.Register<WebViewGoBackMessage>(this, OnWebViewGoBack);
        _messengerService.Register<WebViewGoForwardMessage>(this, OnWebViewGoForward);
    }

    public void OnKeyboardShortcut(string key, bool ctrlKey, bool shiftKey, bool altKey)
    {
        var keyboardShortcutService = ServiceLocator.AcquireService<IKeyboardShortcutService>();
        keyboardShortcutService.HandleShortcut(key, ctrlKey, shiftKey, altKey);
    }

    private void TryNavigate()
    {
        if (_webView?.CoreWebView2 is null)
        {
            return;
        }

        var navigateUrl = ViewModel.NavigateUrl;
        if (string.IsNullOrEmpty(navigateUrl))
        {
            return;
        }

        _webView.CoreWebView2.Navigate(navigateUrl);
    }

    private async void OnWebViewNavigate(object recipient, WebViewNavigateMessage message)
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

    private async void OnWebViewRefresh(object recipient, WebViewRefreshMessage message)
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

    private async void OnWebViewGoBack(object recipient, WebViewGoBackMessage message)
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

    private async void OnWebViewGoForward(object recipient, WebViewGoForwardMessage message)
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

    private async void WebViewDocumentView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= WebViewDocumentView_Loaded;

        // async void is required for a Loaded handler. Any exception in init
        // must be caught here so a faulty load cannot crash the process.
        try
        {
            _webView = await _webViewFactory.AcquireAsync();
            AppWebViewContainer.Children.Add(_webView);

            _webView.CoreWebView2.Settings.AreDevToolsEnabled = _webViewService.IsDevToolsFeatureEnabled();

            if (Options.Role == WebViewDocumentRole.HtmlViewer)
            {
                MapProjectVirtualHost(_webView.CoreWebView2);
            }

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

            AttachNavigationPolicy(_webView.CoreWebView2);

            TryNavigate();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize WebView document view");
            TeardownWebViewState();
        }
    }

    private void MapProjectVirtualHost(CoreWebView2 coreWebView)
    {
        var projectFolder = ResourceRegistry.ProjectFolderPath;
        if (string.IsNullOrEmpty(projectFolder))
        {
            _logger.LogWarning("Cannot map project virtual host: project folder path is empty");
            return;
        }

        coreWebView.SetVirtualHostNameToFolderMapping(
            "project.celbridge",
            projectFolder,
            CoreWebView2HostResourceAccessKind.Allow);
    }

    private void AttachNavigationPolicy(CoreWebView2 coreWebView)
    {
        _navigationPolicy = _serviceProvider.GetRequiredService<IWebViewNavigationPolicy>();

        NavigationDestinationHandler handler;
        if (Options.InterceptTopFrameNavigation)
        {
            handler = CreateInterceptingHandler();
        }
        else
        {
            handler = (_) => Task.FromResult(NavigationDecision.Allow);
        }

        _navigationPolicy.Attach(coreWebView, handler);
    }

    private NavigationDestinationHandler CreateInterceptingHandler()
    {
        return async (destination) =>
        {
            // The HTML viewer is pinned to the project virtual-host URL; allow the
            // initial navigation, reloads, and any same-document scrolling, but prompt
            // the user for any other top-frame destination so the page cannot redirect
            // out from under them.
            var pinnedUrl = ViewModel.NavigateUrl;
            if (!string.IsNullOrEmpty(pinnedUrl) && IsSameDocument(destination, pinnedUrl))
            {
                return NavigationDecision.Allow;
            }

            return await PromptForNavigationDestinationAsync(destination);
        };
    }

    private static bool IsSameDocument(Uri destination, string pinnedUrl)
    {
        if (!Uri.TryCreate(pinnedUrl, UriKind.Absolute, out var pinned))
        {
            return false;
        }

        return string.Equals(destination.Scheme, pinned.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(destination.Host, pinned.Host, StringComparison.OrdinalIgnoreCase)
            && destination.Port == pinned.Port
            && string.Equals(destination.AbsolutePath, pinned.AbsolutePath, StringComparison.Ordinal);
    }

    private async Task<NavigationDecision> PromptForNavigationDestinationAsync(Uri destination)
    {
        try
        {
            var dialogService = _serviceProvider.GetRequiredService<IDialogService>();
            var stringLocalizer = _serviceProvider.GetRequiredService<IStringLocalizer>();

            var title = stringLocalizer.GetString("WebView_NavigationPrompt_Title");
            var message = stringLocalizer.GetString("WebView_NavigationPrompt_Message", destination.ToString());
            var openInBrowserOption = stringLocalizer.GetString("WebView_NavigationPrompt_OpenInBrowser");

            var options = new List<string> { openInBrowserOption };

            var dialogResult = await dialogService.ShowChoiceDialogAsync(title, message, options, defaultIndex: 0);
            if (dialogResult.IsFailure)
            {
                return NavigationDecision.Cancel;
            }

            var choice = dialogResult.Value;
            if (choice.SelectedIndex == 0)
            {
                return NavigationDecision.OpenInSystemBrowser;
            }

            return NavigationDecision.Cancel;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prompt for navigation destination");
            return NavigationDecision.Cancel;
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

            if (_navigationPolicy is not null)
            {
                _navigationPolicy.Detach(_webView.CoreWebView2);
            }
        }

        if (_webView is not null)
        {
            AppWebViewContainer.Children.Remove(_webView);
            _webView.Close();
            _webView = null;
        }

        _navigationPolicy = null;

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

        var message = new WebViewNavigationStateChangedMessage(
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
        // Push the role onto the view model so NavigateUrl knows which URL to compute.
        ViewModel.Role = Options.Role;

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
