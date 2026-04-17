using Celbridge.Commands;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.UserInterface;
using Celbridge.WebView;
using Celbridge.WebView.Services;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Code.Views;

/// <summary>
/// Manages the preview panel lifecycle for CodeEditorDocumentView.
/// Owns the preview WebView2, JSON-RPC host, and all preview-related state.
/// Implements IHostCodePreview to handle notifications from the preview JavaScript.
/// </summary>
public sealed class CodeEditorPreviewHelper : IHostCodePreview, IDisposable
{
    private readonly ILogger<CodeEditorPreviewHelper> _logger;
    private readonly ICommandService _commandService;
    private readonly IWebViewFactory _webViewFactory;
    private readonly IUserInterfaceService _userInterfaceService;

    private readonly Grid _previewContainer;
    private readonly Func<Task<string>> _getEditorContent;
    private readonly Func<double, Task> _scrollEditor;

    private WebView2? _previewWebView;
    private WebViewHostChannel? _previewHostChannel;
    private CelbridgeHost? _previewHost;
    private bool _isPreviewInitialized;
    private bool _isPreviewUpdateInProgress;
    private string _lastPreviewContent = string.Empty;
    private ICodePreviewRenderer? _previewRenderer;
    private ResourceKey _fileResource;
    private double _lastPreviewScrollPercentage;
    private double? _pendingInitialScrollPercentage;

    /// <summary>
    /// The most recent scroll position reported by the preview, as a percentage (0.0 to 1.0).
    /// Updated when the preview scrolls or when Monaco forwards a scroll position to it.
    /// </summary>
    public double LastScrollPercentage => _lastPreviewScrollPercentage;
    private string _projectFolderPath = string.Empty;
    private string _documentPath = string.Empty;

    /// <summary>
    /// Whether the preview WebView has been initialized and is ready for content updates.
    /// </summary>
    public bool IsInitialized => _isPreviewInitialized;

    /// <summary>
    /// Whether a preview renderer has been configured.
    /// </summary>
    public bool IsConfigured => _previewRenderer is not null;

    public CodeEditorPreviewHelper(
        ILogger<CodeEditorPreviewHelper> logger,
        ICommandService commandService,
        IWebViewFactory webViewFactory,
        IUserInterfaceService userInterfaceService,
        Grid previewContainer,
        Func<Task<string>> getEditorContent,
        Func<double, Task> scrollEditor)
    {
        _logger = logger;
        _commandService = commandService;
        _webViewFactory = webViewFactory;
        _userInterfaceService = userInterfaceService;
        _previewContainer = previewContainer;
        _getEditorContent = getEditorContent;
        _scrollEditor = scrollEditor;
    }

    /// <summary>
    /// Configures the helper with a preview renderer.
    /// Call this before InitializeAsync() to enable the preview panel.
    /// </summary>
    public void Configure(ICodePreviewRenderer previewRenderer)
    {
        _previewRenderer = previewRenderer;
    }

    /// <summary>
    /// Sets the file resource, project folder path, and document path for resolving relative resources.
    /// </summary>
    public void SetPaths(ResourceKey fileResource, string projectFolderPath, string documentPath)
    {
        _fileResource = fileResource;
        _projectFolderPath = projectFolderPath;
        _documentPath = documentPath;
    }

    /// <summary>
    /// Updates the file resource and document path for resolving relative resources in the preview.
    /// </summary>
    public void UpdateDocumentPath(ResourceKey fileResource, string documentPath)
    {
        _fileResource = fileResource;
        _documentPath = documentPath;

        if (_isPreviewInitialized && _previewHost is not null)
        {
            var basePath = _fileResource.GetParent().ToString();
            _ = _previewHost.NotifyCodePreviewSetBasePathAsync(basePath);
        }
    }

    /// <summary>
    /// Initializes the preview WebView and navigates to the preview page.
    /// If already initialized, triggers a content update instead.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_previewRenderer is null)
        {
            _logger.LogWarning("Cannot initialize preview: no preview renderer configured");
            return;
        }

        if (_isPreviewInitialized)
        {
            await UpdateAsync();
            return;
        }

        try
        {
            _previewWebView = await _webViewFactory.AcquireAsync();
            _previewContainer.Children.Add(_previewWebView);

            // Set up shared celbridge-client mapping for the preview to use celbridge.js
            _previewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "celbridge-client.celbridge",
                "Celbridge.WebView/Web/celbridge-client",
                CoreWebView2HostResourceAccessKind.Allow);

            // Allow the renderer to configure virtual host mappings for preview and project assets
            await _previewRenderer.ConfigureWebViewAsync(_previewWebView.CoreWebView2, _projectFolderPath);

            // Set up JSON-RPC host for preview communication
            _previewHostChannel = new WebViewHostChannel(_previewWebView.CoreWebView2);
            _previewHost = new CelbridgeHost(_previewHostChannel);

            // Register this helper as the handler for code preview notifications
            _previewHost.AddLocalRpcTarget<IHostCodePreview>(this);
            _previewHost.StartListening();

            // Apply theme
            ApplyTheme();

            // Navigate to preview page
            _previewWebView.CoreWebView2.Navigate(_previewRenderer.PreviewPageUrl);

            // Wait for navigation to complete
            var taskCompletionSource = new TaskCompletionSource();
            void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
            {
                _previewWebView.CoreWebView2.NavigationCompleted -= NavigationCompleted;
                taskCompletionSource.TrySetResult();
            }
            _previewWebView.CoreWebView2.NavigationCompleted += NavigationCompleted;

            var timeout = Task.Delay(TimeSpan.FromSeconds(5));
            var completed = await Task.WhenAny(taskCompletionSource.Task, timeout);

            if (completed == timeout)
            {
                _logger.LogWarning("Preview navigation timed out");
            }

            _isPreviewInitialized = true;

            // Set document context via JSON-RPC
            var basePath = _fileResource.GetParent().ToString();
            await _previewHost.NotifyCodePreviewSetBasePathAsync(basePath);

            // Initial preview update
            await UpdateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize preview");
        }
    }

    /// <summary>
    /// Updates the preview content by fetching the latest editor content.
    /// </summary>
    public async Task UpdateAsync()
    {
        if (_previewHost is null || !_isPreviewInitialized)
        {
            return;
        }

        if (_isPreviewUpdateInProgress)
        {
            return;
        }

        _isPreviewUpdateInProgress = true;

        try
        {
            var content = await _getEditorContent();

            if (content == _lastPreviewContent)
            {
                return;
            }

            _lastPreviewContent = content;

            await _previewHost.NotifyCodePreviewUpdateAsync(content);

            // Apply any scroll position that was requested before the preview was ready.
            // Content has just been pushed, so the preview's scroll height will be valid shortly.
            if (_pendingInitialScrollPercentage is double pendingScroll)
            {
                _pendingInitialScrollPercentage = null;
                await _previewHost.NotifyCodePreviewScrollAsync(pendingScroll);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to update preview");
        }
        finally
        {
            _isPreviewUpdateInProgress = false;
        }
    }

    /// <summary>
    /// Forwards a scroll position change from the editor to the preview.
    /// </summary>
    public void NotifyScrollPositionChanged(double scrollPercentage)
    {
        _lastPreviewScrollPercentage = scrollPercentage;

        if (_isPreviewInitialized && _previewHost is not null)
        {
            _ = _previewHost.NotifyCodePreviewScrollAsync(scrollPercentage);
        }
    }

    /// <summary>
    /// Applies the current application theme to the preview WebView.
    /// </summary>
    public void ApplyTheme()
    {
        if (_previewWebView?.CoreWebView2 is null)
        {
            return;
        }

        var theme = _userInterfaceService.UserInterfaceTheme;
        _previewWebView.CoreWebView2.Profile.PreferredColorScheme = theme == UserInterfaceTheme.Dark
            ? CoreWebView2PreferredColorScheme.Dark
            : CoreWebView2PreferredColorScheme.Light;
    }

    /// <summary>
    /// Cleans up the preview WebView and RPC infrastructure.
    /// </summary>
    public async Task CleanupAsync()
    {
        _previewHost?.Dispose();
        _previewHostChannel?.Detach();

        _previewHost = null;
        _previewHostChannel = null;

        if (_previewWebView is not null)
        {
            _previewWebView.Close();
            _previewWebView = null;
        }

        _isPreviewInitialized = false;

        await Task.CompletedTask;
    }

    #region IHostCodePreview

    public void OnOpenResource(string href)
    {
        if (string.IsNullOrEmpty(_documentPath))
        {
            return;
        }

        var documentFolder = Path.GetDirectoryName(_documentPath);
        if (string.IsNullOrEmpty(documentFolder))
        {
            return;
        }

        var fullPath = Path.GetFullPath(Path.Combine(documentFolder, href));

        if (!fullPath.StartsWith(_projectFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning($"Link path is outside project folder: {href}");
            return;
        }

        var resourcePath = fullPath.Substring(_projectFolderPath.Length).TrimStart(Path.DirectorySeparatorChar);
        var resourceKeyString = resourcePath.Replace(Path.DirectorySeparatorChar, '/');
        if (!ResourceKey.TryCreate(resourceKeyString, out var resourceKey))
        {
            _logger.LogWarning($"Invalid resource key derived from link: {resourceKeyString}");
            return;
        }

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = resourceKey;
        });
    }

    public void OnOpenExternal(string href)
    {
        _commandService.Execute<IOpenBrowserCommand>(command =>
        {
            command.URL = href;
        });
    }

    public void OnSyncToEditor(double scrollPercentage)
    {
        _lastPreviewScrollPercentage = scrollPercentage;
        _ = _scrollEditor(scrollPercentage);
    }

    /// <summary>
    /// Scrolls the preview to the given percentage (0.0 to 1.0).
    /// If the preview is not yet initialized or its content is not yet rendered,
    /// the scroll is deferred and applied after the next content update.
    /// </summary>
    public void ScrollToPercentage(double scrollPercentage)
    {
        _lastPreviewScrollPercentage = scrollPercentage;

        // Defer the scroll until after the preview has initialized and content has been rendered.
        // Without this, a scroll sent before the first content update gets lost because the
        // preview's scroll height is 0.
        if (!_isPreviewInitialized || string.IsNullOrEmpty(_lastPreviewContent))
        {
            _pendingInitialScrollPercentage = scrollPercentage;
            return;
        }

        if (_previewHost is not null)
        {
            _ = _previewHost.NotifyCodePreviewScrollAsync(scrollPercentage);
        }
    }

    #endregion

    public void Dispose()
    {
        _previewHost?.Dispose();
        _previewHostChannel?.Detach();
    }
}
