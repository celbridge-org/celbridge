using System.Text.Json;
using Celbridge.Code.ViewModels;
using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.UserInterface.Views.Controls;
using Celbridge.WebView;
using Celbridge.Workspace;

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
/// Optionally supports a preview panel (configured via ICodePreviewRenderer) with split/preview/source view modes.
/// Accepts custom toolbar content via the CustomToolbar property (e.g., Markdown snippet buttons).
/// </summary>
public sealed partial class CodeEditorDocumentView : DocumentView
{
    private const int ReloadDebounceMilliseconds = 300;

    private readonly ILogger<CodeEditorDocumentView> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IDocumentsService _documentsService;

    private readonly CodeEditorViewModel _viewModel;

    private CodeEditorPreviewHelper? _previewHelper;

    private readonly ColumnDefinition _editorColumn;
    private readonly ColumnDefinition _splitterColumn;
    private readonly ColumnDefinition _previewColumn;
    private readonly Splitter _splitter;
    private readonly Grid _previewContainer;
    private SplitterHelper? _splitterHelper;
    private CancellationTokenSource? _reloadDebounceCancellation;
    private int _reloadDebounceVersion;
    private double _lastScrollPercentage;
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
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;

        _viewModel = ServiceLocator.AcquireService<CodeEditorViewModel>();

        // Set up content loader callback for the editor to pull content when needed
        MonacoEditor.ContentLoader = LoadContentFromDiskAsync;

        // Subscribe to CodeEditor events
        MonacoEditor.ContentChanged += OnMonacoContentChanged;
        MonacoEditor.ContentLoadRequested += OnMonacoContentLoadRequested;
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
    public void ConfigurePreview(ICodePreviewRenderer previewRenderer)
    {
        _previewHelper = new CodeEditorPreviewHelper(
            ServiceLocator.AcquireService<ILogger<CodeEditorPreviewHelper>>(),
            ServiceLocator.AcquireService<ICommandService>(),
            ServiceLocator.AcquireService<IWebViewFactory>(),
            ServiceLocator.AcquireService<IUserInterfaceService>(),
            _previewContainer,
            MonacoEditor.GetContentAsync,
            MonacoEditor.ScrollToPercentageAsync);

        _previewHelper.Configure(previewRenderer);

        // Disable scroll-beyond-last-line for proper scroll sync with preview
        MonacoEditor.Options = new CodeEditorOptions { ScrollBeyondLastLine = false };

        // Show view mode buttons
        ViewModePanel.Visibility = Visibility.Visible;
        UpdateToolbarVisibility();
    }

    /// <summary>
    /// Updates the document path for resolving relative resources in the preview.
    /// </summary>
    public void UpdateDocumentPath(ResourceKey fileResource, string documentPath)
    {
        _previewHelper?.UpdateDocumentPath(fileResource, documentPath);
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
        if (_previewHelper is not null)
        {
            _previewHelper.SetPaths(fileResource, ResourceRegistry.ProjectFolderPath, DocumentViewModel.FilePath);
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
        if (_previewHelper is not null && InitialViewMode.HasValue)
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
            if (ViewMode == SplitEditorViewMode.Preview && _previewHelper is not null)
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

    public override async Task<string?> SaveEditorStateAsync()
    {
        try
        {
            var state = new Dictionary<string, object>
            {
                ["scrollPercentage"] = _lastScrollPercentage,
                ["viewMode"] = ViewMode.ToString()
            };

            return JsonSerializer.Serialize(state);
        }
        catch
        {
            return null;
        }
    }

    public override async Task RestoreEditorStateAsync(string state)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(state);
            if (parsed is null)
            {
                return;
            }

            if (parsed.TryGetValue("viewMode", out var viewModeElement) &&
                Enum.TryParse<SplitEditorViewMode>(viewModeElement.GetString(), out var viewMode))
            {
                if (InitialViewMode.HasValue)
                {
                    SetViewMode(viewMode);
                }
            }

            if (parsed.TryGetValue("scrollPercentage", out var scrollElement))
            {
                var scrollPercentage = scrollElement.GetDouble();
                await MonacoEditor.ScrollToPercentageAsync(scrollPercentage);
            }
        }
        catch
        {
            // Ignore incompatible or corrupt state
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

            // Cancel and dispose any pending reload debounce timer
            _reloadDebounceCancellation?.Cancel();
            _reloadDebounceCancellation?.Dispose();
            _reloadDebounceCancellation = null;

            _messengerService.UnregisterAll(this);

            // Cleanup ViewModel
            _viewModel.Cleanup();

            // Cleanup code editor control
            await MonacoEditor.CleanupAsync();

            // Cleanup preview
            if (_previewHelper is not null)
            {
                await _previewHelper.CleanupAsync();
                _previewHelper.Dispose();
            }
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
        if (IsPreviewVisible && _previewHelper is not null)
        {
            _ = _previewHelper.UpdateAsync();
        }
    }

    private void OnMonacoContentLoadRequested()
    {
        // The JS client has requested a content reload (during an external file change).
        // After LoadAsync returns, the JS will call setValue with the new content, followed by
        // a double-requestAnimationFrame to restore editor state. We schedule a deferred preview
        // update to run after this sequence completes.
        // The normal OnMonacoContentChanged path doesn't fire during external reloads because
        // Monaco suppresses content change notifications to avoid marking the document as unsaved.
        if (IsPreviewVisible && _previewHelper is not null)
        {
            var previewHelper = _previewHelper;
            _ = Task.Delay(500).ContinueWith(_ =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    _ = previewHelper.UpdateAsync();
                });
            }, TaskScheduler.Default);
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
        _lastScrollPercentage = scrollPercentage;

        // Only sync scroll in Split mode where both editor and preview are visible.
        // In Preview mode the editor is collapsed, so its scroll position is always
        // zero and forwarding it would reset the preview scroll to the top.
        if (ViewMode == SplitEditorViewMode.Split)
        {
            _previewHelper?.NotifyScrollPositionChanged(scrollPercentage);
        }
    }

    private void OnViewModelReloadRequested(object? sender, EventArgs e)
    {
        // Cancel any in-flight debounce and start a new one
        _reloadDebounceCancellation?.Cancel();
        _reloadDebounceCancellation = new CancellationTokenSource();
        var cancellationToken = _reloadDebounceCancellation.Token;
        var capturedVersion = ++_reloadDebounceVersion;

        _ = Task.Delay(ReloadDebounceMilliseconds, cancellationToken).ContinueWith(delayTask =>
        {
            if (delayTask.IsCanceled)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (capturedVersion != _reloadDebounceVersion)
                {
                    return;
                }

                MonacoEditor.NotifyExternalChange();
            });
        }, TaskScheduler.Default);
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
                if (_previewHelper is not null)
                {
                    _ = _previewHelper.InitializeAsync();
                }
                break;

            case SplitEditorViewMode.Preview:
                MonacoEditor.Visibility = Visibility.Collapsed;
                _editorColumn.Width = new GridLength(0);
                _splitterColumn.Width = new GridLength(0);
                _previewColumn.Width = new GridLength(1, GridUnitType.Star);
                _splitter.Visibility = Visibility.Collapsed;
                _previewContainer.Visibility = Visibility.Visible;
                if (_previewHelper is not null)
                {
                    _ = _previewHelper.InitializeAsync();
                }
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

    #region Theme

    private void OnThemeChanged(object recipient, ThemeChangedMessage message)
    {
        _previewHelper?.ApplyTheme();
    }

    #endregion
}
