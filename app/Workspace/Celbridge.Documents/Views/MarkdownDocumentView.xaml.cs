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

public sealed partial class MarkdownDocumentView : WebView2DocumentView
{
    // Payload for loading a markdown document into the editor
    private record LoadDocPayload(string Content, string ProjectBaseUrl, string DocumentBaseUrl);

    // Payload for returning a picked resource key to the editor
    private record ResourceKeyPayload(string ResourceKey);

    private readonly ILogger _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDialogService _dialogService;

    public MarkdownDocumentViewModel ViewModel { get; }

    protected override ResourceKey FileResource => ViewModel.FileResource;

    private WebView2Messenger? _webMessenger;

    public MarkdownDocumentView(
        IServiceProvider serviceProvider,
        ILogger<MarkdownDocumentView> logger,
        ICommandService commandService,
        IMessengerService messengerService,
        IUserInterfaceService userInterfaceService,
        IWorkspaceWrapper workspaceWrapper,
        IStringLocalizer stringLocalizer,
        IDialogService dialogService)
        : base(messengerService)
    {
        ViewModel = serviceProvider.GetRequiredService<MarkdownDocumentViewModel>();

        _logger = logger;
        _commandService = commandService;
        _messengerService = messengerService;
        _userInterfaceService = userInterfaceService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        _stringLocalizer = stringLocalizer;
        _dialogService = dialogService;

        this.InitializeComponent();

        // Assign the WebView from XAML to the base class property
        WebView = MarkdownWebView;

        _messengerService.Register<ThemeChangedMessage>(this, OnThemeChanged);

        Loaded += MarkdownDocumentView_Loaded;

        ViewModel.ReloadRequested += ViewModel_ReloadRequested;
    }

    public override bool HasUnsavedChanges => ViewModel.HasUnsavedChanges;

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return ViewModel.UpdateSaveTimer(deltaTime);
    }

    public override async Task<Result> SaveDocument()
    {
        Guard.IsNotNull(_webMessenger);

        if (!TryBeginSave())
        {
            _logger.LogDebug("Save already in progress, queuing pending save");
            return Result.Ok();
        }

        var message = new JsMessage("request-save");
        _webMessenger.Send(message);

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
            Guard.IsNotNull(WebView);

            await WebView.EnsureCoreWebView2Async();

            _webMessenger = new WebView2Messenger(WebView.CoreWebView2);

            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "markdown.celbridge",
                "Celbridge.Documents/Web/Markdown",
                CoreWebView2HostResourceAccessKind.Allow);

            WebView2Helper.MapSharedAssets(WebView.CoreWebView2);

            // Map the project folder so resource key image paths resolve correctly
            var projectFolder = _resourceRegistry.ProjectFolderPath;
            if (!string.IsNullOrEmpty(projectFolder))
            {
                WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "project.celbridge",
                    projectFolder,
                    CoreWebView2HostResourceAccessKind.Allow);
            }

            // Sync WebView2 color scheme with the app theme so CSS prefers-color-scheme matches
            ApplyThemeToWebView(WebView);

            WebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            var settings = WebView.CoreWebView2.Settings;
            settings.AreDevToolsEnabled = true;
            settings.AreDefaultContextMenusEnabled = true;

            await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");

            // Cancel all navigations except the initial editor page load.
            // Link handling is done via JS popover and explicit 'link-clicked' messages.
            WebView.NavigationStarting += (s, args) =>
            {
                var uri = args.Uri;
                if (string.IsNullOrEmpty(uri))
                {
                    return;
                }

                // Allow only the initial editor page load
                if (uri.StartsWith("https://markdown.celbridge/index.html"))
                {
                    return;
                }

                // Cancel all other navigations - the editor should never navigate away
                args.Cancel = true;
            };

            // Block all new window requests - links are handled via JS popover
            WebView.CoreWebView2.NewWindowRequested += (s, args) =>
            {
                args.Handled = true;
            };

            WebView.CoreWebView2.Navigate("https://markdown.celbridge/index.html");

            var isEditorReady = false;
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

            WebView.WebMessageReceived += onWebMessageReceived;

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

            WebView.WebMessageReceived -= onWebMessageReceived;

            // Initialize base WebView2 functionality (keyboard shortcuts, focus handling)
            // Must be done AFTER editor-ready to avoid the base handler receiving initialization messages
            await InitializeWebViewAsync();

            // Send localization strings to the editor before loading content
            _webMessenger.SendLocalizationStrings(_stringLocalizer, "Markdown_");

            await LoadMarkdownContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Markdown Web View.");
        }
    }

    private async Task LoadMarkdownContent()
    {
        Guard.IsNotNull(WebView);
        Guard.IsNotNull(_webMessenger);

        var filePath = ViewModel.FilePath;

        if (!File.Exists(filePath))
        {
            _logger.LogWarning($"Markdown: Cannot load - file does not exist: {filePath}");
            return;
        }

        try
        {
            var content = await ViewModel.LoadMarkdownContent();
            const string projectBaseUrl = "https://project.celbridge/";
            var documentBaseUrl = GetDocumentBaseUrl();

            var payload = new LoadDocPayload(content, projectBaseUrl, documentBaseUrl);
            var message = new JsPayloadMessage<LoadDocPayload>("load-doc", payload);
            _webMessenger.Send(message);
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
        var title = _stringLocalizer.GetString("Markdown_SelectImage_Title");
        var result = await _dialogService.ShowResourcePickerDialogAsync(imageExtensions, title, showPreview: true);

        var relativePath = string.Empty;
        if (result.IsSuccess)
        {
            var resourceKey = result.Value.ToString();
            relativePath = GetRelativePathFromResourceKey(resourceKey);
        }

        var payload = new ResourceKeyPayload(relativePath);
        var message = new JsPayloadMessage<ResourceKeyPayload>("pick-image-resource-result", payload);
        _webMessenger!.Send(message);
    }

    private async Task HandlePickLinkResource()
    {
        var title = _stringLocalizer.GetString("Markdown_SelectFile_Title");
        var result = await _dialogService.ShowResourcePickerDialogAsync(Array.Empty<string>(), title);

        var relativePath = string.Empty;
        if (result.IsSuccess)
        {
            var resourceKey = result.Value.ToString();
            relativePath = GetRelativePathFromResourceKey(resourceKey);
        }

        var payload = new ResourceKeyPayload(relativePath);
        var message = new JsPayloadMessage<ResourceKeyPayload>("pick-link-resource-result", payload);
        _webMessenger!.Send(message);
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

    /// <summary>
    /// Normalizes a path by resolving '..' and '.' segments.
    /// This is used for both link and image relative path resolution.
    /// </summary>
    private static string NormalizeResourcePath(string path)
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

    /// <summary>
    /// Gets the base path (folder) of the current document for resolving relative paths.
    /// Returns an empty string if the document is at the project root.
    /// </summary>
    private string GetDocumentBasePath()
    {
        var fileResourcePath = ViewModel.FileResource.ToString();
        var directoryName = Path.GetDirectoryName(fileResourcePath);
        return directoryName?.Replace('\\', '/') ?? "";
    }

    /// <summary>
    /// Gets the full URL base for the current document's folder.
    /// Used by JavaScript to resolve relative image/link paths.
    /// </summary>
    private string GetDocumentBaseUrl()
    {
        const string projectBaseUrl = "https://project.celbridge/";
        var documentBasePath = GetDocumentBasePath();
        return string.IsNullOrEmpty(documentBasePath)
            ? projectBaseUrl
            : $"{projectBaseUrl}{documentBasePath}/";
    }

    /// <summary>
    /// Converts an absolute Resource Key to a path relative to the current document.
    /// Uses forward slashes only for consistency.
    /// </summary>
    private string GetRelativePathFromResourceKey(string resourceKey)
    {
        if (string.IsNullOrEmpty(resourceKey))
        {
            return string.Empty;
        }

        var documentBasePath = GetDocumentBasePath();
        if (string.IsNullOrEmpty(documentBasePath))
        {
            // Document is at project root, Resource Key is already relative
            return resourceKey;
        }

        var documentSegments = documentBasePath.Split('/');
        var targetSegments = resourceKey.Split('/');

        // Find common prefix length
        var commonLength = 0;
        var minLength = Math.Min(documentSegments.Length, targetSegments.Length);
        for (var i = 0; i < minLength; i++)
        {
            if (documentSegments[i] == targetSegments[i])
            {
                commonLength++;
            }
            else
            {
                break;
            }
        }

        // Build relative path: go up for remaining document segments, then down to target
        var upCount = documentSegments.Length - commonLength;
        var relativeParts = new List<string>();

        for (var i = 0; i < upCount; i++)
        {
            relativeParts.Add("..");
        }

        for (var i = commonLength; i < targetSegments.Length; i++)
        {
            relativeParts.Add(targetSegments[i]);
        }

        return string.Join("/", relativeParts);
    }

    /// <summary>
    /// Resolves a path to an absolute Resource Key.
    /// Paths starting with '/' are resolved from the project root.
    /// All other paths are resolved relative to the current document's folder.
    /// </summary>
    private Result<ResourceKey> ResolveResourcePath(string path)
    {
        string fullPath;

        if (path.StartsWith('/'))
        {
            // Project-root-relative path: strip the leading '/' and use as-is
            fullPath = path.Substring(1);
        }
        else
        {
            // Document-relative path: prepend document's folder
            var documentBasePath = GetDocumentBasePath();
            fullPath = string.IsNullOrEmpty(documentBasePath)
                ? path
                : $"{documentBasePath}/{path}";
        }

        var normalizedPath = NormalizeResourcePath(fullPath);
        var resourceKey = new ResourceKey(normalizedPath);
        var result = _resourceRegistry.NormalizeResourceKey(resourceKey);

        if (result.IsSuccess)
        {
            return Result<ResourceKey>.Ok(result.Value);
        }

        return Result<ResourceKey>.Fail($"Could not resolve resource path: {path}");
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

        // Resolve the path relative to the current document's folder
        try
        {
            var resolveResult = ResolveResourcePath(href);
            if (resolveResult.IsSuccess)
            {
                _commandService.Execute<IOpenDocumentCommand>(command =>
                {
                    command.FileResource = resolveResult.Value;
                });
                return;
            }

            // Could not resolve the link
            var errorTitle = _stringLocalizer.GetString("Markdown_LinkError_Title");
            var errorMessage = _stringLocalizer.GetString("Markdown_LinkError_Message", href);
            await _dialogService.ShowAlertDialogAsync(errorTitle, errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to handle link click for '{href}': {ex.Message}");
            var errorTitle = _stringLocalizer.GetString("Markdown_LinkError_Title");
            var errorMessage = _stringLocalizer.GetString("Markdown_LinkError_Message", href);
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

    protected override async void OnWebMessageReceived(string? webMessage)
    {
        if (string.IsNullOrEmpty(webMessage))
        {
            _logger.LogError("Invalid web message received");
            return;
        }

        // Try to handle as a global keyboard shortcut first
        base.OnWebMessageReceived(webMessage);

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
                    if (IsSaveInProgress)
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

        // Check if there's a pending save that needs processing
        if (CompleteSave())
        {
            _logger.LogDebug("Processing pending save request");
            ViewModel.OnDataChanged();
        }
    }

    public override async Task PrepareToClose()
    {
        _messengerService.UnregisterAll(this);

        Loaded -= MarkdownDocumentView_Loaded;

        ViewModel.ReloadRequested -= ViewModel_ReloadRequested;

        ViewModel.Cleanup();

        _webMessenger = null;

        await base.PrepareToClose();
    }

    private void OnThemeChanged(object recipient, ThemeChangedMessage message)
    {
        if (WebView is not null)
        {
            ApplyThemeToWebView(WebView);
        }
    }

    private void ApplyThemeToWebView(WebView2 webView)
    {
        var theme = _userInterfaceService.UserInterfaceTheme;
        webView.CoreWebView2.Profile.PreferredColorScheme = theme == UserInterfaceTheme.Dark
            ? CoreWebView2PreferredColorScheme.Dark
            : CoreWebView2PreferredColorScheme.Light;
    }

    private async void ViewModel_ReloadRequested(object? sender, EventArgs e)
    {
        if (WebView is not null)
        {
            await LoadMarkdownContent();
        }
    }
}
