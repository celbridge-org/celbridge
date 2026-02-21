using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Celbridge.Documents.Views;

public sealed partial class MilkdownDocumentView : DocumentView
{
    private ILogger _logger;
    private ICommandService _commandService;
    private IMessengerService _messengerService;
    private IResourceRegistry _resourceRegistry;

    public MilkdownDocumentViewModel ViewModel { get; }

    private WebView2? _webView;

    // Track save state to prevent race conditions
    private bool _isSaveInProgress = false;
    private bool _hasPendingSave = false;

    public MilkdownDocumentView(
        IServiceProvider serviceProvider,
        ILogger<MilkdownDocumentView> logger,
        ICommandService commandService,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        ViewModel = serviceProvider.GetRequiredService<MilkdownDocumentViewModel>();

        _logger = logger;
        _commandService = commandService;
        _messengerService = messengerService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        Loaded += MilkdownDocumentView_Loaded;

        ViewModel.ReloadRequested += ViewModel_ReloadRequested;

        this.DataContext(ViewModel);
    }

    public override bool HasUnsavedChanges => ViewModel.HasUnsavedChanges;

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return ViewModel.UpdateSaveTimer(deltaTime);
    }

    public override async Task<Result> SaveDocument()
    {
        Guard.IsNotNull(_webView);

        if (_isSaveInProgress)
        {
            _hasPendingSave = true;
            _logger.LogDebug("Save already in progress, queuing pending save");
            return Result.Ok();
        }

        _isSaveInProgress = true;
        _hasPendingSave = false;

        _webView.CoreWebView2.PostWebMessageAsString("request_save");

        return await ViewModel.SaveDocument();
    }

    private async void MilkdownDocumentView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MilkdownDocumentView_Loaded;

        await InitMilkdownViewAsync();
    }

    private async Task InitMilkdownViewAsync()
    {
        try
        {
            _logger.LogTrace("Milkdown: Creating WebView2 instance");

            var webView = new WebView2();
            await webView.EnsureCoreWebView2Async();

            _logger.LogTrace("Milkdown: WebView2 CoreWebView2 initialized");

            webView.CoreWebView2.SetVirtualHostNameToFolderMapping("milkdown.celbridge",
                "Celbridge.Documents/Web/Milkdown",
                CoreWebView2HostResourceAccessKind.Allow);

            // Map the project folder so relative image/link paths resolve correctly
            var folder = System.IO.Path.GetDirectoryName(ViewModel.FilePath);
            if (!string.IsNullOrEmpty(folder))
            {
                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "project.celbridge",
                    folder,
                    CoreWebView2HostResourceAccessKind.Allow);
            }

            webView.DefaultBackgroundColor = Colors.Transparent;

            webView.CoreWebView2.Settings.IsWebMessageEnabled = true;

            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");

            await WebView2Helper.InjectKeyboardShortcutHandlerAsync(webView.CoreWebView2);

            // Open external links in system browser
            webView.NavigationStarting += (s, args) =>
            {
                var uri = args.Uri;
                if (uri != null && !uri.StartsWith("https://milkdown.celbridge"))
                {
                    args.Cancel = true;
                    OpenSystemBrowser(uri);
                }
            };

            webView.CoreWebView2.NewWindowRequested += (s, args) =>
            {
                args.Handled = true;
                var uri = args.Uri;
                OpenSystemBrowser(uri);
            };

            webView.GotFocus += WebView_GotFocus;

            _webView = webView;

            // Show the WebView immediately so the diagnostic status is visible
            this.Content = _webView;

            _logger.LogTrace("Milkdown: Navigating to index.html");

            webView.CoreWebView2.Navigate("https://milkdown.celbridge/index.html");

            bool isEditorReady = false;
            TypedEventHandler<WebView2, CoreWebView2WebMessageReceivedEventArgs> onWebMessageReceived = (sender, e) =>
            {
                var message = e.TryGetWebMessageAsString();

                _logger.LogTrace($"Milkdown: Received web message during init: '{message}'");

                if (message == "editor_ready")
                {
                    isEditorReady = true;
                    return;
                }

                _logger.LogError($"Milkdown: Expected 'editor_ready' message, but received: '{message}'");
            };

            webView.WebMessageReceived += onWebMessageReceived;

            // Wait for editor_ready with a timeout to avoid hanging indefinitely
            var timeout = TimeSpan.FromSeconds(30);
            var elapsed = TimeSpan.Zero;
            var interval = TimeSpan.FromMilliseconds(50);

            while (!isEditorReady)
            {
                await Task.Delay(interval);
                elapsed += interval;
                if (elapsed >= timeout)
                {
                    _logger.LogError("Milkdown: Timed out waiting for 'editor_ready' message from WebView");
                    return;
                }
            }

            webView.WebMessageReceived -= onWebMessageReceived;

            _logger.LogTrace("Milkdown: Editor is ready, loading content");

            await LoadMarkdownContent();

            _logger.LogTrace("Milkdown: Markdown content loaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Milkdown Web View.");
        }
    }

    private async Task LoadMarkdownContent()
    {
        Guard.IsNotNull(_webView);

        var filePath = ViewModel.FilePath;
        _logger.LogTrace($"Milkdown: Loading markdown from '{filePath}'");

        if (!File.Exists(filePath))
        {
            _logger.LogWarning($"Milkdown: Cannot load markdown - file does not exist: {filePath}");
            return;
        }

        try
        {
            var markdownContent = await File.ReadAllTextAsync(filePath);

            _logger.LogTrace($"Milkdown: Read {markdownContent.Length} chars, posting to WebView");

            _webView.CoreWebView2.PostWebMessageAsString(markdownContent);

            _webView.WebMessageReceived -= WebView_WebMessageReceived;
            _webView.WebMessageReceived += WebView_WebMessageReceived;

            _logger.LogTrace("Milkdown: Content posted to WebView");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to load markdown: {filePath}");
        }
    }

    private void OpenSystemBrowser(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return;
        }

        _commandService.Execute<IOpenBrowserCommand>(command =>
        {
            command.URL = uri;
        });
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

    private async void WebView_WebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var webMessage = args.TryGetWebMessageAsString();
        if (string.IsNullOrEmpty(webMessage))
        {
            _logger.LogError("Invalid web message received");
            return;
        }

        if (WebView2Helper.HandleKeyboardShortcut(webMessage))
        {
            return;
        }

        if (webMessage == "data_changed")
        {
            ViewModel.OnDataChanged();
            return;
        }

        // Only accept markdown content when a save was actually requested.
        // This prevents stray messages (e.g. "editor_ready" arriving late)
        // from being written to the file as content.
        if (_isSaveInProgress)
        {
            await SaveMarkdown(webMessage);
        }
        else
        {
            _logger.LogWarning($"Received unexpected web message while no save was in progress: '{webMessage[..Math.Min(50, webMessage.Length)]}'");
        }
    }

    private async Task SaveMarkdown(string markdownContent)
    {
        var saveResult = await ViewModel.SaveMarkdownToFile(markdownContent);

        if (saveResult.IsFailure)
        {
            _logger.LogError(saveResult, "Failed to save markdown data");
        }

        _isSaveInProgress = false;

        if (_hasPendingSave)
        {
            _logger.LogDebug("Processing pending save request");
            _hasPendingSave = false;
            ViewModel.OnDataChanged();
        }
    }

    public override async Task PrepareToClose()
    {
        Loaded -= MilkdownDocumentView_Loaded;

        ViewModel.ReloadRequested -= ViewModel_ReloadRequested;

        ViewModel.Cleanup();

        if (_webView != null)
        {
            _webView.WebMessageReceived -= WebView_WebMessageReceived;
            _webView.GotFocus -= WebView_GotFocus;

            _webView.Close();
            _webView = null;
        }

        await base.PrepareToClose();
    }

    private void WebView_GotFocus(object sender, RoutedEventArgs e)
    {
        var message = new DocumentViewFocusedMessage(ViewModel.FileResource);
        _messengerService.Send(message);
    }

    private async void ViewModel_ReloadRequested(object? sender, EventArgs e)
    {
        if (_webView != null)
        {
            await LoadMarkdownContent();
        }
    }
}
