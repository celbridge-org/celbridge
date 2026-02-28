using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Documents.Views;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.WebApp.ViewModels;
using Celbridge.Workspace;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebApp.Views;

public sealed partial class WebAppDocumentView : WebView2DocumentView
{
    private readonly ILogger<WebAppDocumentView> _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private IResourceRegistry ResourceRegistry => _workspaceWrapper.WorkspaceService.ResourceService.Registry;

    public WebAppDocumentViewModel ViewModel { get; }

    protected override ResourceKey FileResource => ViewModel.FileResource;

    public WebAppDocumentView(
        IServiceProvider serviceProvider,
        ILogger<WebAppDocumentView> logger,
        ICommandService commandService,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
        : base(messengerService)
    {
        this.InitializeComponent();

        _logger = logger;
        _commandService = commandService;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;

        ViewModel = serviceProvider.GetRequiredService<WebAppDocumentViewModel>();

        // Assign the WebView from XAML to the base class property
        WebView = AppWebView;

        Loaded += WebAppDocumentView_Loaded;

        _messengerService.Register<WebAppNavigateMessage>(this, OnWebAppNavigate);
        _messengerService.Register<WebAppRefreshMessage>(this, OnWebAppRefresh);
        _messengerService.Register<WebAppGoBackMessage>(this, OnWebAppGoBack);
        _messengerService.Register<WebAppGoForwardMessage>(this, OnWebAppGoForward);
    }

    private async void OnWebAppNavigate(object recipient, WebAppNavigateMessage message)
    {
        if (message.DocumentResource != ViewModel.FileResource)
        {
            return;
        }

        if (WebView is null)
        {
            return;
        }

        try
        {
            await WebView.EnsureCoreWebView2Async();
            WebView.CoreWebView2.Navigate(message.Url);
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

        if (WebView is null)
        {
            return;
        }

        try
        {
            await WebView.EnsureCoreWebView2Async();

            // Clear the cache before refreshing to ensure fresh content
            await WebView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                CoreWebView2BrowsingDataKinds.CacheStorage | CoreWebView2BrowsingDataKinds.DiskCache);

            WebView.CoreWebView2.Reload();
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

        if (WebView is null)
        {
            return;
        }

        try
        {
            await WebView.EnsureCoreWebView2Async();
            if (WebView.CoreWebView2.CanGoBack)
            {
                WebView.CoreWebView2.GoBack();
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

        if (WebView is null)
        {
            return;
        }

        try
        {
            await WebView.EnsureCoreWebView2Async();
            if (WebView.CoreWebView2.CanGoForward)
            {
                WebView.CoreWebView2.GoForward();
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

        await InitWebAppViewAsync();
    }

    private async Task InitWebAppViewAsync()
    {
        // Initialize base WebView2 functionality (keyboard shortcuts, focus handling)
        await InitializeWebViewAsync();

        Guard.IsNotNull(WebView);

        // Ensure we only register once for these events
        WebView.CoreWebView2.DownloadStarting -= CoreWebView2_DownloadStarting;
        WebView.CoreWebView2.DownloadStarting += CoreWebView2_DownloadStarting;

        WebView.CoreWebView2.NewWindowRequested -= WebView_NewWindowRequested;
        WebView.CoreWebView2.NewWindowRequested += WebView_NewWindowRequested;

        WebView.CoreWebView2.HistoryChanged -= CoreWebView2_HistoryChanged;
        WebView.CoreWebView2.HistoryChanged += CoreWebView2_HistoryChanged;

        WebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
        WebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

        // Navigate to the URL if already loaded
        if (!string.IsNullOrEmpty(ViewModel.SourceUrl))
        {
            WebView.CoreWebView2.Navigate(ViewModel.SourceUrl);
        }
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
        if (WebView is null)
        {
            return;
        }

        var coreWebView = WebView.CoreWebView2;

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

        //
        // Map the download path to a unique path in the project folder 
        //
        var requestedPath = ResourceRegistry.GetResourcePath(filename);
        var getResult = PathHelper.GetUniquePath(requestedPath);
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
        var getResourceResult = ResourceRegistry.GetResourceKey(savePath);
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
        var tempPath = PathHelper.GetTemporaryFilePath("Downloads", extension);
        args.ResultFilePath = tempPath;

        //
        // Handle download state changes
        //
        args.DownloadOperation.StateChanged += (s, e) =>
        {
            if (s.State == CoreWebView2DownloadState.Completed)
            {
                // Move the file to the requested path, with undo support.
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
        var filePath = ResourceRegistry.GetResourcePath(fileResource);

        if (ResourceRegistry.GetResource(fileResource).IsFailure)
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

        // Load URL from file - actual navigation happens in InitWebAppViewAsync when the view is loaded
        var loadResult = await ViewModel.LoadContent();
        if (loadResult.IsFailure)
        {
            return loadResult;
        }

        // If the WebView is already initialized (reload case), navigate now
        if (WebView?.CoreWebView2 is not null && !string.IsNullOrEmpty(ViewModel.SourceUrl))
        {
            WebView.CoreWebView2.Navigate(ViewModel.SourceUrl);
        }

        return loadResult;
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
        _messengerService.UnregisterAll(this);

        if (WebView?.CoreWebView2 is not null)
        {
            WebView.CoreWebView2.DownloadStarting -= CoreWebView2_DownloadStarting;
            WebView.CoreWebView2.NewWindowRequested -= WebView_NewWindowRequested;
            WebView.CoreWebView2.HistoryChanged -= CoreWebView2_HistoryChanged;
            WebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
        }

        await base.PrepareToClose();
    }
}
