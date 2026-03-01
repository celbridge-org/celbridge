using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Markdown.ViewModels;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Markdown.Views;

public sealed partial class MarkdownDocumentView : WebView2DocumentView
{
    private readonly ILogger _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDialogService _dialogService;

    public MarkdownDocumentViewModel ViewModel { get; }

    protected override ResourceKey FileResource => ViewModel.FileResource;

    private WebViewBridge? _bridge;
    private WebView2MessageChannel? _messageChannel;

    // Track dirty state for external change conflict resolution
    private bool _isDirty;

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
        if (_bridge is null)
        {
            _logger.LogDebug("Save skipped - bridge not initialized");
            return Result.Ok();
        }

        if (!TryBeginSave())
        {
            _logger.LogDebug("Save already in progress, queuing pending save");
            return Result.Ok();
        }

        // Request the JS side to save - it will call document.save(content)
        // which triggers our OnSaveDocument handler
        _bridge.Document.RequestSave();

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

            // Set up virtual host mappings
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "markdown.celbridge",
                "Celbridge.Markdown/Web/Markdown",
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

            // Sync WebView2 color scheme with the app theme
            ApplyThemeToWebView(WebView);

            WebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            var settings = WebView.CoreWebView2.Settings;
            settings.AreDevToolsEnabled = true;
            settings.AreDefaultContextMenusEnabled = true;

            await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.isWebView = true;");

            // Cancel all navigations except the initial editor page load
            WebView.NavigationStarting += (s, args) =>
            {
                var uri = args.Uri;
                if (string.IsNullOrEmpty(uri))
                {
                    return;
                }

                if (uri.StartsWith("https://markdown.celbridge/index.html"))
                {
                    return;
                }

                args.Cancel = true;
            };

            // Block all new window requests
            WebView.CoreWebView2.NewWindowRequested += (s, args) =>
            {
                args.Handled = true;
            };

            // Initialize the bridge BEFORE navigation
            _messageChannel = new WebView2MessageChannel(WebView.CoreWebView2);
            _bridge = new WebViewBridge(_messageChannel);

            // Register bridge handlers
            _bridge.OnInitialize(HandleInitializeAsync);
            _bridge.Document.OnSave(HandleSaveDocumentAsync);
            _bridge.Document.OnLoad(HandleLoadDocumentAsync);
            _bridge.Document.OnChanged(OnDocumentChanged);
            _bridge.Document.OnLinkClicked(HandleLinkClicked);
            _bridge.Dialog.OnPickImage(HandlePickImageAsync);
            _bridge.Dialog.OnPickFile(HandlePickFileAsync);

            // Navigate to the editor
            WebView.CoreWebView2.Navigate("https://markdown.celbridge/index.html");

            // The bridge initialization is now handled by the JS side calling bridge.initialize()
            // which triggers our HandleInitializeAsync handler. We don't need to wait for
            // editor-ready separately - the bridge handles the handshake.

            // Initialize base WebView2 functionality (keyboard shortcuts, focus handling)
            await InitializeWebViewAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Markdown Web View.");
        }
    }

    private async Task<InitializeResult> HandleInitializeAsync(InitializeParams request)
    {
        // Validate protocol version
        if (request.ProtocolVersion != "1.0")
        {
            throw new BridgeException(
                JsonRpcErrorCodes.InvalidVersion,
                $"Unsupported protocol version: {request.ProtocolVersion}. Expected: 1.0");
        }

        // Load content from file
        var content = await ViewModel.LoadMarkdownContent();

        // Build metadata
        var metadata = new DocumentMetadata(
            ViewModel.FilePath,
            ViewModel.FileResource.ToString(),
            Path.GetFileName(ViewModel.FilePath));

        // Gather localization strings
        var localization = WebViewLocalizationHelper.GetLocalizedStrings(_stringLocalizer, "Markdown_");

        // Build theme info
        var isDark = _userInterfaceService.UserInterfaceTheme == UserInterfaceTheme.Dark;
        var theme = new ThemeInfo(isDark ? "Dark" : "Light", isDark);

        return new InitializeResult(content, metadata, localization, theme);
    }

    private async Task<SaveResult> HandleSaveDocumentAsync(SaveParams request)
    {
        try
        {
            var content = request.Content;
            var saveResult = await ViewModel.SaveMarkdownToFile(content);

            if (saveResult.IsFailure)
            {
                _logger.LogError(saveResult, "Failed to save markdown data");
                CompleteSave();
                return new SaveResult(false, saveResult.Error);
            }

            // Check if there's a pending save that needs processing
            if (CompleteSave())
            {
                _logger.LogDebug("Processing pending save request");
                ViewModel.OnDataChanged();
            }

            _isDirty = false;
            return new SaveResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during save");
            CompleteSave();
            return new SaveResult(false, ex.Message);
        }
    }

    private async Task<LoadResult> HandleLoadDocumentAsync(LoadParams request)
    {
        var content = await ViewModel.LoadMarkdownContent();

        DocumentMetadata? metadata = null;
        if (request.IncludeMetadata)
        {
            metadata = new DocumentMetadata(
                ViewModel.FilePath,
                ViewModel.FileResource.ToString(),
                Path.GetFileName(ViewModel.FilePath));
        }

        return new LoadResult(content, metadata);
    }

    private void OnDocumentChanged()
    {
        _isDirty = true;
        ViewModel.OnDataChanged();
    }

    private async Task<PickImageResult> HandlePickImageAsync(PickImageParams request)
    {
        var extensions = request.Extensions;
        if (extensions is null || extensions.Length == 0)
        {
            extensions =
            [
                ".png",
                ".jpg",
                ".jpeg",
                ".gif",
                ".webp",
                ".svg",
                ".bmp"
            ];
        }

        var title = _stringLocalizer.GetString("Markdown_SelectImage_Title");
        var result = await _dialogService.ShowResourcePickerDialogAsync(extensions, title, showPreview: true);

        if (result.IsSuccess)
        {
            var resourceKey = result.Value.ToString();
            var relativePath = GetRelativePathFromResourceKey(resourceKey);
            return new PickImageResult(relativePath);
        }

        return new PickImageResult(null);
    }

    private async Task<PickFileResult> HandlePickFileAsync(PickFileParams request)
    {
        var title = _stringLocalizer.GetString("Markdown_SelectFile_Title");
        var result = await _dialogService.ShowResourcePickerDialogAsync(request.Extensions ?? [], title);

        if (result.IsSuccess)
        {
            var resourceKey = result.Value.ToString();
            var relativePath = GetRelativePathFromResourceKey(resourceKey);
            return new PickFileResult(relativePath);
        }

        return new PickFileResult(null);
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

    private void HandleLinkClicked(string href)
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

            // Could not resolve the link - show error asynchronously
            _ = ShowLinkErrorAsync(href);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to handle link click for '{href}': {ex.Message}");
            _ = ShowLinkErrorAsync(href);
        }
    }

    private async Task ShowLinkErrorAsync(string href)
    {
        var errorTitle = _stringLocalizer.GetString("Markdown_LinkError_Title");
        var errorMessage = _stringLocalizer.GetString("Markdown_LinkError_Message", href);
        await _dialogService.ShowAlertDialogAsync(errorTitle, errorMessage);
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
        _messengerService.UnregisterAll(this);

        Loaded -= MarkdownDocumentView_Loaded;

        ViewModel.ReloadRequested -= ViewModel_ReloadRequested;

        ViewModel.Cleanup();

        // Detach the message channel to stop receiving messages
        _messageChannel?.Detach();
        _bridge?.Dispose();
        _bridge = null;
        _messageChannel = null;

        await base.PrepareToClose();
    }

    private void OnThemeChanged(object recipient, ThemeChangedMessage message)
    {
        if (WebView is not null)
        {
            ApplyThemeToWebView(WebView);

            // Notify the JS side of theme change
            if (_bridge is not null)
            {
                var isDark = _userInterfaceService.UserInterfaceTheme == UserInterfaceTheme.Dark;
                var theme = new ThemeInfo(isDark ? "Dark" : "Light", isDark);
                _bridge.Theme.NotifyChanged(theme);
            }
        }
    }

    private void ApplyThemeToWebView(WebView2 webView)
    {
        var theme = _userInterfaceService.UserInterfaceTheme;
        webView.CoreWebView2.Profile.PreferredColorScheme = theme == UserInterfaceTheme.Dark
            ? CoreWebView2PreferredColorScheme.Dark
            : CoreWebView2PreferredColorScheme.Light;
    }

    private void ViewModel_ReloadRequested(object? sender, EventArgs e)
    {
        // External file change detected - notify JS to reload
        // The dirty state conflict handling is done in the ViewModel before raising this event
        _bridge?.Document.NotifyExternalChange();
    }
}
