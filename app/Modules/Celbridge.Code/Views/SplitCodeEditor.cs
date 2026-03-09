using Celbridge.Commands;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.UserInterface.Views.Controls;
using Celbridge.WebView;
using Celbridge.WebView.Services;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Code.Views;

/// <summary>
/// Controls how the SplitCodeEditor arranges its editor and preview panels.
/// </summary>
public enum SplitEditorViewMode
{
    Source,
    Split,
    Preview
}

/// <summary>
/// A reusable control that combines a Monaco code editor with an optional preview panel.
/// The preview panel is rendered by an IPreviewRenderer implementation.
/// Uses JSON-RPC via CelbridgeHost for communication with the preview WebView.
/// </summary>
public sealed partial class SplitCodeEditor : UserControl, IHostPreview
{
    private readonly ILogger<SplitCodeEditor> _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IWebViewFactory _webViewFactory;
    private readonly IUserInterfaceService _userInterfaceService;

    private WebView2? _previewWebView;
    private WebViewHostChannel? _previewHostChannel;
    private CelbridgeHost? _previewHost;
    private bool _isPreviewInitialized;
    private bool _isPreviewUpdateInProgress;
    private string _lastPreviewContent = string.Empty;
    private IPreviewRenderer? _previewRenderer;
    private string _projectFolderPath = string.Empty;
    private string _documentPath = string.Empty;

    // Grid components
    private readonly Grid _rootGrid;
    private readonly ColumnDefinition _editorColumn;
    private readonly ColumnDefinition _splitterColumn;
    private readonly ColumnDefinition _previewColumn;
    private readonly Splitter _splitter;
    private readonly Grid _previewContainer;
    private SplitterHelper? _splitterHelper;
    private double _editorRatio = 1.0;
    private double _previewRatio = 1.0;
    private double _totalDragDelta;
    private const double MinDragDistance = 3.0;
    private const int MinSectionSize = 200;

    /// <summary>
    /// The Monaco editor control.
    /// </summary>
    public MonacoEditorControl MonacoEditor { get; }

    /// <summary>
    /// The current view mode of the editor.
    /// </summary>
    public SplitEditorViewMode ViewMode { get; private set; } = SplitEditorViewMode.Source;

    /// <summary>
    /// Whether the preview panel is currently visible.
    /// </summary>
    public bool IsPreviewVisible => ViewMode == SplitEditorViewMode.Split || ViewMode == SplitEditorViewMode.Preview;

    /// <summary>
    /// Raised when the content changes in the Monaco editor.
    /// </summary>
    public event Action? ContentChanged;

    /// <summary>
    /// Raised when the editor receives focus.
    /// </summary>
    public event Action? EditorFocused;

    public SplitCodeEditor()
    {
        _logger = ServiceLocator.AcquireService<ILogger<SplitCodeEditor>>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _webViewFactory = ServiceLocator.AcquireService<IWebViewFactory>();
        _userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();

        // Create the Monaco editor
        MonacoEditor = new MonacoEditorControl();
        MonacoEditor.ContentChanged += OnMonacoContentChanged;
        MonacoEditor.EditorFocused += OnMonacoEditorFocused;
        MonacoEditor.ScrollPositionChanged += OnMonacoScrollPositionChanged;

        // Build the UI programmatically
        _rootGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _editorColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        _splitterColumn = new ColumnDefinition { Width = new GridLength(0) };
        _previewColumn = new ColumnDefinition { Width = new GridLength(0) };

        _rootGrid.ColumnDefinitions.Add(_editorColumn);
        _rootGrid.ColumnDefinitions.Add(_splitterColumn);
        _rootGrid.ColumnDefinitions.Add(_previewColumn);

        // Add Monaco editor
        Grid.SetColumn(MonacoEditor, 0);
        _rootGrid.Children.Add(MonacoEditor);

        // Create splitter
        _splitter = new Splitter
        {
            Orientation = Orientation.Vertical,
            Visibility = Visibility.Collapsed
        };
        _splitter.DragStarted += OnSplitterDragStarted;
        _splitter.DragDelta += OnSplitterDragDelta;
        _splitter.DragCompleted += OnSplitterDragCompleted;
        _splitter.DoubleClicked += OnSplitterDoubleClicked;
        Grid.SetColumn(_splitter, 1);
        _rootGrid.Children.Add(_splitter);

        // Create preview container
        _previewContainer = new Grid
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(_previewContainer, 2);
        _rootGrid.Children.Add(_previewContainer);

        this.Content = _rootGrid;

        // Subscribe to theme changes
        _messengerService.Register<ThemeChangedMessage>(this, OnThemeChanged);
    }

    /// <summary>
    /// Configures the split editor with a preview renderer.
    /// Call this before showing the preview.
    /// </summary>
    public void ConfigurePreview(IPreviewRenderer previewRenderer, string projectFolderPath, string documentPath)
    {
        _previewRenderer = previewRenderer;
        _projectFolderPath = projectFolderPath;
        _documentPath = documentPath;

        // Disable scroll-beyond-last-line for proper scroll sync with preview
        MonacoEditor.Options = new MonacoEditorOptions { ScrollBeyondLastLine = false };
    }

    /// <summary>
    /// Sets the view mode, updating the editor and preview panel layout accordingly.
    /// </summary>
    public void SetViewMode(SplitEditorViewMode viewMode)
    {
        if (viewMode == ViewMode)
        {
            return;
        }

        ViewMode = viewMode;
        UpdatePreviewLayout(viewMode);
    }

    /// <summary>
    /// Updates the document path for resolving relative resources.
    /// </summary>
    public void UpdateDocumentPath(string documentPath)
    {
        _documentPath = documentPath;

        if (_isPreviewInitialized && _previewRenderer is not null && _previewHost is not null)
        {
            var basePath = _previewRenderer.ComputeBasePath(_documentPath, _projectFolderPath);
            _ = _previewHost.NotifyPreviewSetContextAsync(basePath);
        }
    }

    /// <summary>
    /// Gets the current content from the Monaco editor.
    /// </summary>
    public Task<string> GetContentAsync() => MonacoEditor.GetContentAsync();

    /// <summary>
    /// Cleans up resources when the control is being disposed.
    /// </summary>
    public async Task CleanupAsync()
    {
        MonacoEditor.ContentChanged -= OnMonacoContentChanged;
        MonacoEditor.EditorFocused -= OnMonacoEditorFocused;
        MonacoEditor.ScrollPositionChanged -= OnMonacoScrollPositionChanged;
        _splitter.DragStarted -= OnSplitterDragStarted;
        _splitter.DragDelta -= OnSplitterDragDelta;
        _splitter.DragCompleted -= OnSplitterDragCompleted;
        _splitter.DoubleClicked -= OnSplitterDoubleClicked;

        _messengerService.UnregisterAll(this);

        await MonacoEditor.CleanupAsync();
        await CleanupPreviewAsync();
    }

    private void OnMonacoContentChanged()
    {
        ContentChanged?.Invoke();

        // Update preview if visible
        if (IsPreviewVisible && !_isPreviewUpdateInProgress)
        {
            _ = UpdatePreviewAsync();
        }
    }

    private void OnMonacoEditorFocused()
    {
        EditorFocused?.Invoke();
    }

    private void OnSplitterDragStarted(object? sender, EventArgs e)
    {
        _totalDragDelta = 0;

        _splitterHelper ??= new SplitterHelper(
            _rootGrid,
            GridResizeMode.Columns,
            firstIndex: 0,
            secondIndex: 2,
            minSize: MinSectionSize);

        _splitterHelper.OnDragStarted();
    }

    private void OnSplitterDragDelta(object? sender, double delta)
    {
        _totalDragDelta += Math.Abs(delta);
        _splitterHelper?.OnDragDelta(delta);
    }

    private void OnSplitterDragCompleted(object? sender, EventArgs e)
    {
        if (_totalDragDelta < MinDragDistance)
        {
            return;
        }

        ConvertColumnsToStar();
    }

    private void OnSplitterDoubleClicked(object? sender, EventArgs e)
    {
        _editorRatio = 1.0;
        _previewRatio = 1.0;

        _editorColumn.Width = new GridLength(1, GridUnitType.Star);
        _previewColumn.Width = new GridLength(1, GridUnitType.Star);
    }

    private void ConvertColumnsToStar()
    {
        var editorWidth = _editorColumn.ActualWidth;
        var previewWidth = _previewColumn.ActualWidth;
        var totalWidth = editorWidth + previewWidth;

        if (totalWidth <= 0)
        {
            return;
        }

        _editorRatio = editorWidth / totalWidth;
        _previewRatio = previewWidth / totalWidth;

        _editorColumn.Width = new GridLength(_editorRatio, GridUnitType.Star);
        _previewColumn.Width = new GridLength(_previewRatio, GridUnitType.Star);
    }

    private void OnMonacoScrollPositionChanged(double scrollPercentage)
    {
        if (IsPreviewVisible && _isPreviewInitialized && _previewHost is not null)
        {
            _ = _previewHost.NotifyPreviewScrollAsync(scrollPercentage);
        }
    }

    private void UpdatePreviewLayout(SplitEditorViewMode viewMode)
    {
        switch (viewMode)
        {
            case SplitEditorViewMode.Source:
                MonacoEditor.Visibility = Visibility.Visible;
                _editorColumn.Width = new GridLength(1, GridUnitType.Star);
                _splitterColumn.Width = new GridLength(0);
                _previewColumn.Width = new GridLength(0);
                _splitter.Visibility = Visibility.Collapsed;
                _previewContainer.Visibility = Visibility.Collapsed;
                break;

            case SplitEditorViewMode.Split:
                MonacoEditor.Visibility = Visibility.Visible;
                _editorColumn.Width = new GridLength(_editorRatio, GridUnitType.Star);
                _splitterColumn.Width = new GridLength(1, GridUnitType.Pixel);
                _previewColumn.Width = new GridLength(_previewRatio, GridUnitType.Star);
                _splitter.Visibility = Visibility.Visible;
                _previewContainer.Visibility = Visibility.Visible;
                _ = InitializePreviewAsync();
                break;

            case SplitEditorViewMode.Preview:
                MonacoEditor.Visibility = Visibility.Collapsed;
                _editorColumn.Width = new GridLength(0);
                _splitterColumn.Width = new GridLength(0);
                _previewColumn.Width = new GridLength(1, GridUnitType.Star);
                _splitter.Visibility = Visibility.Collapsed;
                _previewContainer.Visibility = Visibility.Visible;
                _ = InitializePreviewAsync();
                break;
        }
    }

    private async Task InitializePreviewAsync()
    {
        if (_previewRenderer is null)
        {
            _logger.LogWarning("Cannot initialize preview: no preview renderer configured");
            return;
        }

        if (_isPreviewInitialized)
        {
            await UpdatePreviewAsync();
            return;
        }

        try
        {
            _previewWebView = await _webViewFactory.AcquireAsync();
            _previewContainer.Children.Add(_previewWebView);

            // Set up virtual host mapping for preview assets
            _previewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                _previewRenderer.PreviewHostName,
                _previewRenderer.PreviewAssetFolder,
                CoreWebView2HostResourceAccessKind.Allow);

            // Set up shared celbridge-client mapping for the preview to use celbridge.js
            _previewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "celbridge-client.celbridge",
                "Celbridge.WebView/Web/celbridge-client",
                CoreWebView2HostResourceAccessKind.Allow);

            // Allow the renderer to configure additional mappings
            await _previewRenderer.ConfigureWebViewAsync(_previewWebView.CoreWebView2, _projectFolderPath);

            // Set up JSON-RPC host for preview communication
            _previewHostChannel = new WebViewHostChannel(_previewWebView.CoreWebView2);
            _previewHost = new CelbridgeHost(_previewHostChannel);

            // Register this control as the handler for preview notifications
            _previewHost.AddLocalRpcTarget<IHostPreview>(this);
            _previewHost.StartListening();

            // Apply theme
            ApplyThemeToPreview();

            // Navigate to preview page
            _previewWebView.CoreWebView2.Navigate(_previewRenderer.PreviewPageUrl);

            // Wait for navigation to complete
            var tcs = new TaskCompletionSource();
            void NavigationCompleted(object? s, CoreWebView2NavigationCompletedEventArgs args)
            {
                _previewWebView.CoreWebView2.NavigationCompleted -= NavigationCompleted;
                tcs.TrySetResult();
            }
            _previewWebView.CoreWebView2.NavigationCompleted += NavigationCompleted;

            var timeout = Task.Delay(TimeSpan.FromSeconds(5));
            var completed = await Task.WhenAny(tcs.Task, timeout);

            if (completed == timeout)
            {
                _logger.LogWarning("Preview navigation timed out");
            }

            _isPreviewInitialized = true;

            // Set document context via JSON-RPC
            var basePath = _previewRenderer.ComputeBasePath(_documentPath, _projectFolderPath);
            await _previewHost.NotifyPreviewSetContextAsync(basePath);

            // Initial preview update
            await UpdatePreviewAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize preview");
        }
    }

    private async Task UpdatePreviewAsync()
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
            var content = await MonacoEditor.GetContentAsync();

            if (content == _lastPreviewContent || !IsPreviewVisible)
            {
                return;
            }

            _lastPreviewContent = content;

            await _previewHost.NotifyPreviewUpdateAsync(content);
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

    private async Task CleanupPreviewAsync()
    {
        // Dispose RPC infrastructure
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

    #region IHostPreview

    public void OnOpenResource(string href)
    {
        if (string.IsNullOrEmpty(_documentPath))
        {
            return;
        }

        var documentDir = Path.GetDirectoryName(_documentPath);
        if (string.IsNullOrEmpty(documentDir))
        {
            return;
        }

        var fullPath = Path.GetFullPath(Path.Combine(documentDir, href));

        if (!fullPath.StartsWith(_projectFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning($"Link path is outside project folder: {href}");
            return;
        }

        var resourcePath = fullPath.Substring(_projectFolderPath.Length).TrimStart(Path.DirectorySeparatorChar);
        var resourceKey = new ResourceKey(resourcePath.Replace(Path.DirectorySeparatorChar, '/'));

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
        _ = MonacoEditor.ScrollToPercentageAsync(scrollPercentage);
    }

    #endregion

    private void OnThemeChanged(object recipient, ThemeChangedMessage message)
    {
        ApplyThemeToPreview();
    }

    private void ApplyThemeToPreview()
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
}
