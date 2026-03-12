using System.Text.Json;
using Celbridge.Code.ViewModels;
using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.UserInterface.Views.Controls;
using Celbridge.WebView;
using Celbridge.WebView.Services;
using Celbridge.Workspace;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Code.Views;

/// <summary>
/// Controls how the CodeEditorDocumentView arranges its editor and preview panels.
/// </summary>
public enum SplitEditorViewMode
{
    Source,
    Split,
    Preview
}

/// <summary>
/// Unified document view for editing code/text files using a Monaco code editor.
/// Optionally supports a preview panel (configured via IPreviewRenderer) with split/preview/source view modes.
/// Accepts custom toolbar content via the CustomToolbar property (e.g., Markdown snippet buttons).
/// </summary>
public sealed partial class CodeEditorDocumentView : DocumentView, IHostCodePreview
{
    private readonly ILogger<CodeEditorDocumentView> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IWebViewFactory _webViewFactory;
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly IDocumentsService _documentsService;

    private readonly CodeEditorViewModel _viewModel;

    // Preview state
    private WebView2? _previewWebView;
    private WebViewHostChannel? _previewHostChannel;
    private CelbridgeHost? _previewHost;
    private bool _isPreviewInitialized;
    private bool _isPreviewUpdateInProgress;
    private string _lastPreviewContent = string.Empty;
    private IPreviewRenderer? _previewRenderer;
    private string _projectFolderPath = string.Empty;
    private string _documentPath = string.Empty;

    // Splitter state
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

    protected override DocumentViewModel DocumentViewModel => _viewModel;

    public override bool HasUnsavedChanges => _viewModel.HasUnsavedChanges;

    /// <summary>
    /// The current view mode of the editor.
    /// </summary>
    public SplitEditorViewMode ViewMode { get; private set; } = SplitEditorViewMode.Source;

    /// <summary>
    /// Whether the preview panel is currently visible.
    /// </summary>
    public bool IsPreviewVisible => ViewMode == SplitEditorViewMode.Split || ViewMode == SplitEditorViewMode.Preview;

    /// <summary>
    /// Raised when the view mode changes.
    /// </summary>
    public event EventHandler<SplitEditorViewMode>? ViewModeChanged;

    /// <summary>
    /// Optional initial view mode to apply after LoadContent() initializes the editor.
    /// Only used when a preview renderer is configured. Default is null (stays in Source mode).
    /// </summary>
    public SplitEditorViewMode? InitialViewMode { get; set; }

    /// <summary>
    /// Sets custom toolbar content (e.g., snippet buttons) displayed alongside the view mode buttons.
    /// Set this before LoadContent() is called.
    /// </summary>
    public UIElement? CustomToolbar
    {
        get => CustomToolbarPresenter.Content as UIElement;
        set
        {
            CustomToolbarPresenter.Content = value;
            UpdateToolbarVisibility();
        }
    }

    /// <summary>
    /// Sets the tooltip for the Preview mode button.
    /// </summary>
    public string? PreviewModeTooltip
    {
        set => ToolTipService.SetToolTip(PreviewModeButton, value);
    }

    /// <summary>
    /// Sets the tooltip for the Split mode button.
    /// </summary>
    public string? SplitModeTooltip
    {
        set => ToolTipService.SetToolTip(SplitModeButton, value);
    }

    /// <summary>
    /// Sets the tooltip for the Source mode button.
    /// </summary>
    public string? SourceModeTooltip
    {
        set => ToolTipService.SetToolTip(SourceModeButton, value);
    }

    /// <summary>
    /// Optional URL to a customization script for the Monaco editor.
    /// Set this before LoadContent() is called.
    /// </summary>
    public string? CustomizationScriptUrl
    {
        get => MonacoEditor.CustomizationScriptUrl;
        set => MonacoEditor.CustomizationScriptUrl = value;
    }

    /// <summary>
    /// Pre-warms the code editor by performing expensive initialization without loading content.
    /// Call this to prepare an instance for fast reuse later.
    /// </summary>
    public async Task<Result> PreWarmAsync()
    {
        return await MonacoEditor.PreInitializeAsync();
    }

    public CodeEditorDocumentView()
    {
        this.InitializeComponent();

        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();

        _logger = ServiceLocator.AcquireService<ILogger<CodeEditorDocumentView>>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _webViewFactory = ServiceLocator.AcquireService<IWebViewFactory>();
        _userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;

        _viewModel = ServiceLocator.AcquireService<CodeEditorViewModel>();

        // Set up content loader callback for the editor to pull content when needed
        MonacoEditor.ContentLoader = LoadContentFromDiskAsync;

        // Subscribe to CodeEditor events
        MonacoEditor.ContentChanged += OnMonacoContentChanged;
        MonacoEditor.EditorFocused += OnMonacoEditorFocused;
        MonacoEditor.ScrollPositionChanged += OnMonacoScrollPositionChanged;

        // Subscribe to ViewModel reload requests (external file changes)
        _viewModel.ReloadRequested += OnViewModelReloadRequested;

        // Build the split/preview layout programmatically
        _editorColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        _splitterColumn = new ColumnDefinition { Width = new GridLength(0) };
        _previewColumn = new ColumnDefinition { Width = new GridLength(0) };

        EditorContainer.ColumnDefinitions.Add(_editorColumn);
        EditorContainer.ColumnDefinitions.Add(_splitterColumn);
        EditorContainer.ColumnDefinitions.Add(_previewColumn);

        // Place the MonacoEditor in the first column
        Grid.SetColumn(MonacoEditor, 0);

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
        EditorContainer.Children.Add(_splitter);

        // Create preview container
        _previewContainer = new Grid
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(_previewContainer, 2);
        EditorContainer.Children.Add(_previewContainer);

        // Subscribe to theme changes for preview
        _messengerService.Register<ThemeChangedMessage>(this, OnThemeChanged);
    }

    #region Preview Configuration

    /// <summary>
    /// Configures the editor with a preview renderer.
    /// Call this before LoadContent() to enable the split editor with preview.
    /// The project folder path and document path are filled in automatically by SetFileResource().
    /// </summary>
    public void ConfigurePreview(IPreviewRenderer previewRenderer)
    {
        _previewRenderer = previewRenderer;

        // Disable scroll-beyond-last-line for proper scroll sync with preview
        MonacoEditor.Options = new CodeEditorOptions { ScrollBeyondLastLine = false };

        // Show view mode buttons
        ViewModePanel.Visibility = Visibility.Visible;
        UpdateToolbarVisibility();
    }

    /// <summary>
    /// Updates the document path for resolving relative resources in the preview.
    /// </summary>
    public void UpdateDocumentPath(string documentPath)
    {
        _documentPath = documentPath;

        if (_isPreviewInitialized && _previewRenderer is not null && _previewHost is not null)
        {
            var basePath = _previewRenderer.ComputeBasePath(_documentPath, _projectFolderPath);
            _ = _previewHost.NotifyCodePreviewSetContextAsync(basePath);
        }
    }

    #endregion

    #region View Mode

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
        UpdateViewModeButtons(viewMode);

        ViewModeChanged?.Invoke(this, viewMode);
    }

    private void PreviewModeButton_Click(object sender, RoutedEventArgs e)
    {
        SetViewMode(SplitEditorViewMode.Preview);
    }

    private void SplitModeButton_Click(object sender, RoutedEventArgs e)
    {
        SetViewMode(SplitEditorViewMode.Split);
    }

    private void SourceModeButton_Click(object sender, RoutedEventArgs e)
    {
        SetViewMode(SplitEditorViewMode.Source);
    }

    private void UpdateViewModeButtons(SplitEditorViewMode viewMode)
    {
        PreviewModeButton.IsChecked = viewMode == SplitEditorViewMode.Preview;
        SplitModeButton.IsChecked = viewMode == SplitEditorViewMode.Split;
        SourceModeButton.IsChecked = viewMode == SplitEditorViewMode.Source;
    }

    #endregion

    #region Document Lifecycle

    public override async Task<Result> SetFileResource(ResourceKey fileResource)
    {
        var result = await base.SetFileResource(fileResource);
        if (result.IsFailure)
        {
            return result;
        }

        // If preview is configured, fill in the paths from the registry
        if (_previewRenderer is not null)
        {
            _projectFolderPath = ResourceRegistry.ProjectFolderPath;
            _documentPath = DocumentViewModel.FilePath;
        }

        return Result.Ok();
    }

    public override async Task<Result> LoadContent()
    {
        // Load file content via ViewModel
        var loadResult = await _viewModel.LoadDocument();
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to load content for resource: {_viewModel.FileResource}")
                .WithErrors(loadResult);
        }

        var content = loadResult.Value;
        var language = _documentsService.GetDocumentLanguage(_viewModel.FileResource);

        // Initialize the editor with the content
        var initResult = await MonacoEditor.InitializeAsync(
            content,
            language,
            _viewModel.FilePath,
            _viewModel.FileResource.ToString());

        if (initResult.IsFailure)
        {
            return initResult;
        }

        // Apply the initial view mode after the editor is ready
        if (_previewRenderer is not null && InitialViewMode.HasValue)
        {
            SetViewMode(InitialViewMode.Value);
        }

        return initResult;
    }

    /// <summary>
    /// Loads content from disk. Used as the ContentLoader callback for the code editor.
    /// </summary>
    private async Task<string> LoadContentFromDiskAsync()
    {
        var loadResult = await _viewModel.LoadDocument();
        if (loadResult.IsFailure)
        {
            _logger.LogError(loadResult, $"Failed to load content for resource: {_viewModel.FileResource}");
            return string.Empty;
        }

        return loadResult.Value;
    }

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return _viewModel.UpdateSaveTimer(deltaTime);
    }

    protected override async Task<Result> SaveDocumentContentAsync()
    {
        try
        {
            // Get current content from the editor
            var content = await MonacoEditor.GetContentAsync();

            // Save via ViewModel
            return await _viewModel.SaveDocumentContent(content);
        }
        catch (Exception ex)
        {
            return Result.Fail("Failed to save document")
                .WithException(ex);
        }
    }

    public override async Task<Result> NavigateToLocation(string location)
    {
        if (string.IsNullOrEmpty(location))
        {
            return Result.Ok();
        }

        try
        {
            // Parse the location JSON to extract start position and optional selection end range
            using var doc = JsonDocument.Parse(location);
            var root = doc.RootElement;

            var lineNumber = root.TryGetProperty("lineNumber", out var lineProp) ? lineProp.GetInt32() : 1;
            var column = root.TryGetProperty("column", out var colProp) ? colProp.GetInt32() : 1;
            var endLineNumber = root.TryGetProperty("endLineNumber", out var endLineProp) ? endLineProp.GetInt32() : 0;
            var endColumn = root.TryGetProperty("endColumn", out var endColProp) ? endColProp.GetInt32() : 0;

            // Switch to Split mode when navigating in Preview mode so the user can see the text selection
            if (ViewMode == SplitEditorViewMode.Preview && _previewRenderer is not null)
            {
                SetViewMode(SplitEditorViewMode.Split);
            }

            return await MonacoEditor.NavigateToLocationAsync(lineNumber, column, endLineNumber, endColumn);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to navigate to location: {location}")
                .WithException(ex);
        }
    }

    public override async Task<Result> ApplyEditsAsync(IEnumerable<TextEdit> edits)
    {
        try
        {
            // Switch to Split mode when applying edits in Preview mode
            if (ViewMode == SplitEditorViewMode.Preview && _previewRenderer is not null)
            {
                SetViewMode(SplitEditorViewMode.Split);
            }

            await MonacoEditor.ApplyEditsAsync(edits);

            // Mark document as having unsaved changes
            _viewModel.OnTextChanged();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("Failed to apply edits to document")
                .WithException(ex);
        }
    }

    public override async Task PrepareToClose()
    {
        try
        {
            // Unsubscribe from events and clear callback
            MonacoEditor.ContentChanged -= OnMonacoContentChanged;
            MonacoEditor.EditorFocused -= OnMonacoEditorFocused;
            MonacoEditor.ScrollPositionChanged -= OnMonacoScrollPositionChanged;
            MonacoEditor.ContentLoader = null;
            _viewModel.ReloadRequested -= OnViewModelReloadRequested;
            _splitter.DragStarted -= OnSplitterDragStarted;
            _splitter.DragDelta -= OnSplitterDragDelta;
            _splitter.DragCompleted -= OnSplitterDragCompleted;
            _splitter.DoubleClicked -= OnSplitterDoubleClicked;

            _messengerService.UnregisterAll(this);

            // Cleanup ViewModel
            _viewModel.Cleanup();

            // Cleanup code editor control
            await MonacoEditor.CleanupAsync();

            // Cleanup preview
            await CleanupPreviewAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while preparing CodeEditorDocumentView to close");
        }
    }

    /// <summary>
    /// Inserts text at the current cursor position in the Monaco editor.
    /// </summary>
    public Task InsertTextAtCaretAsync(string text) => MonacoEditor.InsertTextAtCaretAsync(text);

    #endregion

    #region Monaco Event Handlers

    private void OnMonacoContentChanged()
    {
        // Mark document as having unsaved changes
        _viewModel.OnTextChanged();

        // Update preview if visible
        if (IsPreviewVisible && !_isPreviewUpdateInProgress)
        {
            _ = UpdatePreviewAsync();
        }
    }

    private void OnMonacoEditorFocused()
    {
        // Notify the system that this document view has focus
        var message = new DocumentViewFocusedMessage(_viewModel.FileResource);
        _messengerService.Send(message);
    }

    private void OnMonacoScrollPositionChanged(double scrollPercentage)
    {
        if (IsPreviewVisible && _isPreviewInitialized && _previewHost is not null)
        {
            _ = _previewHost.NotifyCodePreviewScrollAsync(scrollPercentage);
        }
    }

    private void OnViewModelReloadRequested(object? sender, EventArgs e)
    {
        // External file change detected - notify the editor to reload
        MonacoEditor.NotifyExternalChange();
    }

    #endregion

    #region Toolbar

    private void UpdateToolbarVisibility()
    {
        var hasViewModeButtons = ViewModePanel.Visibility == Visibility.Visible;
        var hasCustomToolbar = CustomToolbarPresenter.Content is not null;

        ToolbarBorder.Visibility = (hasViewModeButtons || hasCustomToolbar)
            ? Visibility.Visible
            : Visibility.Collapsed;

        ToolbarSeparator.Visibility = (hasViewModeButtons && hasCustomToolbar)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    #endregion

    #region Preview Layout

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

    #endregion

    #region Splitter

    private void OnSplitterDragStarted(object? sender, EventArgs e)
    {
        _totalDragDelta = 0;

        _splitterHelper ??= new SplitterHelper(
            EditorContainer,
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

    #endregion

    #region Preview Initialization

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

            // Register this control as the handler for code preview notifications
            _previewHost.AddLocalRpcTarget<IHostCodePreview>(this);
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
            await _previewHost.NotifyCodePreviewSetContextAsync(basePath);

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

            await _previewHost.NotifyCodePreviewUpdateAsync(content);
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

    #endregion

    #region IHostCodePreview

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

    #region Theme

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

    #endregion
}
