using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Celbridge.Documents.Views;

public sealed partial class MarkdownDocumentView : DocumentView
{
    // Payload for loading a markdown document into the editor
    private record LoadDocPayload(string Content, string ProjectBaseUrl);

    // Payload for returning a picked resource key to the editor
    private record ResourceKeyPayload(string ResourceKey);

    private ILogger _logger;
    private ICommandService _commandService;
    private IMessengerService _messengerService;
    private IUserInterfaceService _userInterfaceService;
    private IResourceRegistry _resourceRegistry;
    private IStringLocalizer _stringLocalizer;
    private IDialogService _dialogService;

    public MarkdownDocumentViewModel ViewModel { get; }

    private WebView2? _webView;
    private WebView2Messenger? _messenger;

    // Track save state to prevent race conditions
    private bool _isSaveInProgress = false;
    private bool _hasPendingSave = false;

    public MarkdownDocumentView(
        IServiceProvider serviceProvider,
        ILogger<MarkdownDocumentView> logger,
        ICommandService commandService,
        IMessengerService messengerService,
        IUserInterfaceService userInterfaceService,
        IWorkspaceWrapper workspaceWrapper,
        IStringLocalizer stringLocalizer,
        IDialogService dialogService)
    {
        ViewModel = serviceProvider.GetRequiredService<MarkdownDocumentViewModel>();

        _logger = logger;
        _commandService = commandService;
        _messengerService = messengerService;
        _userInterfaceService = userInterfaceService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        _stringLocalizer = stringLocalizer;
        _dialogService = dialogService;

        _messengerService.Register<ThemeChangedMessage>(this, OnThemeChanged);

        Loaded += MarkdownDocumentView_Loaded;

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
        Guard.IsNotNull(_messenger);

        if (_isSaveInProgress)
        {
            _hasPendingSave = true;
            _logger.LogDebug("Save already in progress, queuing pending save");
            return Result.Ok();
        }

        _isSaveInProgress = true;
        _hasPendingSave = false;

        var message = new JsMessage("request-save");
        _messenger.Send(message);

        return await ViewModel.SaveDocument();
    }

    private async void MarkdownDocumentView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MarkdownDocumentView_Loaded;

        await InitMarkdownViewAsync();
    }

    private async Task InitMarkdownViewAsync()
    {
        try
        {
            var webView = new WebView2();
            await webView.EnsureCoreWebView2Async();

            var messenger = new WebView2Messenger(webView.CoreWebView2);

            webView.CoreWebView2.SetVirtualHostNameToFolderMapping("note.celbridge",
                "Celbridge.Documents/Web/Note",
                CoreWebView2HostResourceAccessKind.Allow);

            WebView2Helper.MapSharedAssets(webView.CoreWebView2);

            // Map the project folder so resource key image paths resolve correctly
            var projectFolder = _resourceRegistry.ProjectFolderPath;
            if (!string.IsNullOrEmpty(projectFolder))
            {
                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "project.celbridge",
                    projectFolder,
                    CoreWebView2HostResourceAccessKind.Allow);
            }

            webView.DefaultBackgroundColor = Colors.Transparent;

            // Sync WebView2 color scheme with the app theme so CSS prefers-color-scheme matches
            ApplyThemeToWebView(webView);

            webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            var settings = webView.CoreWebView2.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = true;

            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");

            await WebView2Helper.InjectKeyboardShortcutHandlerAsync(webView.CoreWebView2);

            // Cancel all navigations except the initial editor page load.
            // Link handling is done via JS popover and explicit 'link-clicked' messages.
            webView.NavigationStarting += (s, args) =>
            {
                var uri = args.Uri;
                if (string.IsNullOrEmpty(uri))
                {
                    return;
                }

                // Allow only the initial editor page load
                if (uri.StartsWith("https://note.celbridge/index.html"))
                {
                    return;
                }

                // Cancel all other navigations - the editor should never navigate away
                args.Cancel = true;
            };

            // Block all new window requests - links are handled via JS popover
            webView.CoreWebView2.NewWindowRequested += (s, args) =>
            {
                args.Handled = true;
            };

            webView.GotFocus += WebView_GotFocus;

            _webView = webView;
            _messenger = messenger;

            // Show the WebView immediately so the status is visible
            this.Content = _webView;

            webView.CoreWebView2.Navigate("https://note.celbridge/index.html");

            bool isEditorReady = false;
            TypedEventHandler<WebView2, CoreWebView2WebMessageReceivedEventArgs> onWebMessageReceived = (sender, e) =>
            {
                var message = e.TryGetWebMessageAsString();

                if (WebView2Helper.HandleKeyboardShortcut(message))
                {
                    return;
                }

                try
                {
                    using var doc = JsonDocument.Parse(message);
                    var type = doc.RootElement.GetProperty("type").GetString();
                    if (type == "editor-ready")
                    {
                        isEditorReady = true;
                        return;
                    }
                }
                catch
                {
                    // Not JSON or doesn't have type field
                }

                _logger.LogError($"Markdown: Expected 'editor-ready' message, but received: '{message}'");
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
                    _logger.LogError("Markdown: Timed out waiting for 'editor-ready' message from WebView");
                    return;
                }
            }

            webView.WebMessageReceived -= onWebMessageReceived;

            // Send localization strings to the editor before loading content
            messenger.SendLocalizationStrings(_stringLocalizer, "NoteEditor_");

            await LoadMarkdownContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Markdown Web View.");
        }
    }

    private async Task LoadMarkdownContent()
    {
        Guard.IsNotNull(_webView);
        Guard.IsNotNull(_messenger);

        var filePath = ViewModel.FilePath;

        if (!File.Exists(filePath))
        {
            _logger.LogWarning($"Markdown: Cannot load - file does not exist: {filePath}");
            return;
        }

        try
        {
            var content = await ViewModel.LoadMarkdownContent();
            var projectBaseUrl = "https://project.celbridge/";

            var payload = new LoadDocPayload(content, projectBaseUrl);
            var message = new JsPayloadMessage<LoadDocPayload>("load-doc", payload);
            _messenger.Send(message);

            _webView.WebMessageReceived -= WebView_WebMessageReceived;
            _webView.WebMessageReceived += WebView_WebMessageReceived;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to load markdown: {filePath}");
        }
    }

    private async Task HandlePickImageResource()
    {
        var imageExtensions = new[]
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".gif",
            ".webp",
            ".svg",
            ".bmp"
        };
        var title = _stringLocalizer.GetString("NoteEditor_SelectImage_Title");
        var result = await _dialogService.ShowResourcePickerDialogAsync(imageExtensions, title, showPreview: true);

        var resourceKey = string.Empty;
        if (result.IsSuccess)
        {
            resourceKey = result.Value.ToString();
        }

        var payload = new ResourceKeyPayload(resourceKey);
        var message = new JsPayloadMessage<ResourceKeyPayload>("pick-image-resource-result", payload);
        _messenger!.Send(message);
    }

    private async Task HandlePickLinkResource()
    {
        var title = _stringLocalizer.GetString("NoteEditor_SelectResource_Title");
        var result = await _dialogService.ShowResourcePickerDialogAsync(Array.Empty<string>(), title);

        var resourceKey = string.Empty;
        if (result.IsSuccess)
        {
            resourceKey = result.Value.ToString();
        }

        var payload = new ResourceKeyPayload(resourceKey);
        var message = new JsPayloadMessage<ResourceKeyPayload>("pick-link-resource-result", payload);
        _messenger!.Send(message);
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

    private async Task HandleLinkClicked(string? href)
    {
        if (string.IsNullOrEmpty(href))
        {
            return;
        }

        // Remote URLs: open in system browser
        if (Uri.TryCreate(href, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https"))
        {
            OpenSystemBrowser(href);
            return;
        }

        // Treat as a resource key — try as-is first, then relative to the current document's folder
        try
        {
            // Helper to normalize path segments (handle ../ and ./)
            string NormalizePath(string path)
            {
                var segments = path.Split('/');
                var stack = new Stack<string>();
                foreach (var segment in segments)
                {
                    if (segment == ".." && stack.Count > 0)
                    {
                        stack.Pop();
                    }
                    else if (segment != "." && !string.IsNullOrEmpty(segment))
                    {
                        stack.Push(segment);
                    }
                }
                return string.Join("/", stack.Reverse());
            }

            // 1. Try href as a direct resource key (absolute from project root)
            var directKey = new ResourceKey(NormalizePath(href));
            var directResult = _resourceRegistry.NormalizeResourceKey(directKey);
            if (directResult.IsSuccess)
            {
                _commandService.Execute<IOpenDocumentCommand>(command =>
                {
                    command.FileResource = directResult.Value;
                });
                return;
            }

            // 2. Try href relative to the current document's folder
            var fileResourcePath = ViewModel.FileResource.ToString();
            var directoryName = Path.GetDirectoryName(fileResourcePath);
            var currentFolder = directoryName?.Replace('\\', '/') ?? "";
            if (!string.IsNullOrEmpty(currentFolder))
            {
                var relativePath = NormalizePath($"{currentFolder}/{href}");
                var relativeKey = new ResourceKey(relativePath);
                var relativeResult = _resourceRegistry.NormalizeResourceKey(relativeKey);
                if (relativeResult.IsSuccess)
                {
                    _commandService.Execute<IOpenDocumentCommand>(command =>
                    {
                        command.FileResource = relativeResult.Value;
                    });
                    return;
                }
            }

            // Could not resolve the link
            var errorTitle = _stringLocalizer.GetString("NoteEditor_LinkError_Title");
            var errorMessage = _stringLocalizer.GetString("NoteEditor_LinkError_Message", href);
            await _dialogService.ShowAlertDialogAsync(errorTitle, errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to handle link click for '{href}': {ex.Message}");
            var errorTitle = _stringLocalizer.GetString("NoteEditor_LinkError_Title");
            var errorMessage = _stringLocalizer.GetString("NoteEditor_LinkError_Message", href);
            await _dialogService.ShowAlertDialogAsync(errorTitle, errorMessage);
        }
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

        try
        {
            using var doc = JsonDocument.Parse(webMessage);
            var type = doc.RootElement.GetProperty("type").GetString();

            switch (type)
            {
                case "doc-changed":
                    ViewModel.OnDataChanged();
                    break;

                case "save-response":
                    if (_isSaveInProgress)
                    {
                        var content = doc.RootElement
                            .GetProperty("payload")
                            .GetProperty("content")
                            .GetString();

                        if (!string.IsNullOrEmpty(content))
                        {
                            await SaveMarkdownContent(content);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Received save-response while no save was in progress");
                    }
                    break;

                case "link-clicked":
                    var href = doc.RootElement
                        .GetProperty("payload")
                        .GetProperty("href")
                        .GetString();
                    await HandleLinkClicked(href);
                    break;

                case "pick-image-resource":
                    await HandlePickImageResource();
                    break;

                case "pick-link-resource":
                    await HandlePickLinkResource();
                    break;

                default:
                    _logger.LogWarning($"Markdown: Received unknown message type: '{type}'");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Markdown: Failed to parse web message: '{webMessage[..Math.Min(50, webMessage.Length)]}'. Exception: {ex.Message}");
        }
    }

    private async Task SaveMarkdownContent(string markdownContent)
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
        _messengerService.UnregisterAll(this);

        Loaded -= MarkdownDocumentView_Loaded;

        ViewModel.ReloadRequested -= ViewModel_ReloadRequested;

        ViewModel.Cleanup();

        if (_webView != null)
        {
            _webView.WebMessageReceived -= WebView_WebMessageReceived;
            _webView.GotFocus -= WebView_GotFocus;

            _webView.Close();
            _webView = null;
            _messenger = null;
        }

        await base.PrepareToClose();
    }

    private void OnThemeChanged(object recipient, ThemeChangedMessage message)
    {
        if (_webView != null)
        {
            ApplyThemeToWebView(_webView);
        }
    }

    private void ApplyThemeToWebView(WebView2 webView)
    {
        var theme = _userInterfaceService.UserInterfaceTheme;
        webView.CoreWebView2.Profile.PreferredColorScheme = theme == UserInterfaceTheme.Dark
            ? CoreWebView2PreferredColorScheme.Dark
            : CoreWebView2PreferredColorScheme.Light;
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
