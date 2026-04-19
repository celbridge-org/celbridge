using System.Text.Json;
using Celbridge.Code.ViewModels;
using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Messaging;
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
/// Optionally enables an in-page split-editor preview (via SetPreviewRenderer) with
/// split/preview/source view modes. Accepts custom toolbar content via the CustomToolbar
/// property (e.g. format-specific snippet buttons).
/// </summary>
public sealed partial class CodeEditorDocumentView : DocumentView
{
    private const int ReloadDebounceMilliseconds = 300;

    private readonly ILogger<CodeEditorDocumentView> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IDocumentsService _documentsService;

    private readonly CodeEditorViewModel _viewModel;

    private string? _pendingEditorStateJson;
    private bool _isEditorReady;
    private bool _hasPreviewRenderer;
    private string? _previewRendererUrl;
    private CancellationTokenSource? _reloadDebounceCancellation;
    private int _reloadDebounceVersion;
    private double _lastScrollPercentage;
    private double _lastPreviewScrollPercentage;

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
    /// Only used when a preview renderer is attached. Default is null (stays in Source mode).
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
        MonacoEditor.EditorFocused += OnMonacoEditorFocused;
        MonacoEditor.ScrollPositionChanged += OnMonacoScrollPositionChanged;
        MonacoEditor.PreviewScrollPositionChanged += OnMonacoPreviewScrollPositionChanged;

        // Subscribe to ViewModel reload requests (external file changes)
        _viewModel.ReloadRequested += OnViewModelReloadRequested;
    }

    /// <summary>
    /// Attaches a split-editor preview renderer, or detaches the current one.
    /// Pass a URL to an ES module that implements the preview contract to enable the
    /// split editor; pass null to disable it. Call this before LoadContent() so the
    /// editor initializes with the renderer wired in from the start.
    /// </summary>
    public void SetPreviewRenderer(string? rendererUrl)
    {
        _previewRendererUrl = rendererUrl;
        _hasPreviewRenderer = !string.IsNullOrEmpty(rendererUrl);

        if (_hasPreviewRenderer)
        {
            // Disable scroll-beyond-last-line for proper scroll sync with the preview pane.
            MonacoEditor.Options = MonacoEditor.Options with
            {
                PreviewRendererUrl = rendererUrl,
                ScrollBeyondLastLine = false
            };
            ViewModePanel.Visibility = Visibility.Visible;
        }
        else
        {
            MonacoEditor.Options = MonacoEditor.Options with { PreviewRendererUrl = null };
            ViewModePanel.Visibility = Visibility.Collapsed;
        }

        UpdateToolbarVisibility();
    }

    /// <summary>
    /// Sets the view mode, updating the Monaco editor and preview pane layout accordingly.
    /// </summary>
    public void SetViewMode(SplitEditorViewMode viewMode)
    {
        if (viewMode == ViewMode)
        {
            return;
        }

        ViewMode = viewMode;
        _ = MonacoEditor.SetViewModeAsync(viewMode.ToString().ToLowerInvariant());
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

    public override async Task<Result> SetFileResource(ResourceKey fileResource)
    {
        var result = await base.SetFileResource(fileResource);
        if (result.IsFailure)
        {
            return result;
        }

        // Push the base path to the preview renderer so relative resources resolve correctly.
        // Harmless when no preview renderer is attached: the call is skipped.
        if (_hasPreviewRenderer && _isEditorReady)
        {
            var basePath = fileResource.GetParent().ToString();
            await MonacoEditor.UpdateBasePathAsync(basePath);
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
            _viewModel.FileResource.ToString(),
            ResourceRegistry.ProjectFolderPath);

        if (initResult.IsFailure)
        {
            return initResult;
        }

        _isEditorReady = true;

        // Push the preview base path now that the editor is ready to receive RPCs
        if (_hasPreviewRenderer)
        {
            var basePath = _viewModel.FileResource.GetParent().ToString();
            await MonacoEditor.UpdateBasePathAsync(basePath);
        }

        // Apply the initial view mode after the editor is ready
        if (_hasPreviewRenderer && InitialViewMode.HasValue)
        {
            SetViewMode(InitialViewMode.Value);
        }

        // Apply any editor state that was deferred because the editor wasn't ready yet
        if (_pendingEditorStateJson is not null)
        {
            var pendingState = _pendingEditorStateJson;
            _pendingEditorStateJson = null;
            await RestoreEditorStateAsync(pendingState);
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
            if (ViewMode == SplitEditorViewMode.Preview && _hasPreviewRenderer)
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

    public override async Task<string?> TrySaveEditorStateAsync()
    {
        await Task.CompletedTask;

        if (!_isEditorReady)
        {
            return null;
        }

        try
        {
            // Save both pane scrolls independently: in Split mode each pane can
            // have its own position (editor-to-preview sync is one-directional),
            // and in Source/Preview mode the hidden pane's last-known value is
            // still what the user will see if they switch modes after reopen.
            var state = new Dictionary<string, object>
            {
                ["editorScrollPercentage"] = _lastScrollPercentage,
                ["previewScrollPercentage"] = _lastPreviewScrollPercentage,
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
        if (!_isEditorReady)
        {
            // Editor not yet initialized. Defer until after LoadContent completes
            _pendingEditorStateJson = state;
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(state);
            if (parsed is null)
            {
                return;
            }

            var restoredViewMode = SplitEditorViewMode.Source;
            if (parsed.TryGetValue("viewMode", out var viewModeElement) &&
                Enum.TryParse<SplitEditorViewMode>(viewModeElement.GetString(), out var parsedViewMode))
            {
                restoredViewMode = parsedViewMode;
                if (InitialViewMode.HasValue)
                {
                    SetViewMode(restoredViewMode);
                }
            }

            // Seed the tracked scroll percentages from restored state directly.
            // The JS-side scroll notifications that normally keep these in sync
            // are suppressed for programmatic scrolls (preview) or skipped when
            // the pane is collapsed (editor in Preview mode), so without this
            // the next save after a restore would persist a stale zero.
            if (parsed.TryGetValue("editorScrollPercentage", out var editorScrollElement))
            {
                var editorScroll = editorScrollElement.GetDouble();
                _lastScrollPercentage = editorScroll;
                await MonacoEditor.ScrollToPercentageAsync(editorScroll);
            }

            if (parsed.TryGetValue("previewScrollPercentage", out var previewScrollElement) &&
                _hasPreviewRenderer &&
                (restoredViewMode == SplitEditorViewMode.Preview ||
                 restoredViewMode == SplitEditorViewMode.Split))
            {
                var previewScroll = previewScrollElement.GetDouble();
                _lastPreviewScrollPercentage = previewScroll;
                await MonacoEditor.ScrollPreviewToPercentageAsync(previewScroll);
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
            MonacoEditor.PreviewScrollPositionChanged -= OnMonacoPreviewScrollPositionChanged;
            MonacoEditor.ContentLoader = null;
            _viewModel.ReloadRequested -= OnViewModelReloadRequested;

            // Cancel and dispose any pending reload debounce timer
            _reloadDebounceCancellation?.Cancel();
            _reloadDebounceCancellation?.Dispose();
            _reloadDebounceCancellation = null;

            _messengerService.UnregisterAll(this);

            // Cleanup ViewModel
            _viewModel.Cleanup();

            // Cleanup code editor control
            await MonacoEditor.CleanupAsync();
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

    private void OnMonacoContentChanged()
    {
        // Mark document as having unsaved changes. Preview updates are driven by monaco.js
        // directly on the JS side; there is no host-side preview refresh to trigger here.
        _viewModel.OnTextChanged();
    }

    private void OnMonacoEditorFocused()
    {
        // Notify the system that this document view has focus
        var message = new DocumentViewFocusedMessage(_viewModel.FileResource);
        _messengerService.Send(message);
    }

    private void OnMonacoScrollPositionChanged(double scrollPercentage)
    {
        // Track the editor's scroll percentage so SaveEditorStateAsync can persist it
        // across document close/reopen and workspace reloads. We deliberately do NOT
        // forward the scroll to the preview pane: source lines and their rendered output
        // don't have matching heights, so percentage-based editor-to-preview sync makes
        // the preview jump around distractingly while the user edits. The reverse
        // direction (preview -> editor via onSyncToEditor in monaco.js) remains active
        // so readers can drag the preview to a section and the editor follows.
        _lastScrollPercentage = scrollPercentage;
    }

    private void OnMonacoPreviewScrollPositionChanged(double scrollPercentage)
    {
        _lastPreviewScrollPercentage = scrollPercentage;
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

                _ = ReloadWithStatePreservationAsync();
            });
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// External-reload orchestration specific to the Monaco editor. Mirrors
    /// WebViewDocumentView.ReloadWithStatePreservationAsync but uses MonacoEditor's host and
    /// ContentLoaded event because CodeEditorDocumentView does not extend WebViewDocumentView
    /// (its WebView is owned by the nested CodeEditor control). The orchestration pattern is the
    /// same so both paths behave identically: capture state, trigger reload, wait for completion,
    /// restore state.
    /// Note: monaco.js also preserves cursor and selection inline in rAF because those aren't
    /// captured by the C# SaveEditorState override yet. That inline preservation is complementary
    /// to the framework orchestration rather than a duplicate.
    /// </summary>
    private async Task ReloadWithStatePreservationAsync()
    {
        string? savedState = null;
        try
        {
            savedState = await TrySaveEditorStateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to capture Monaco editor state before external reload; continuing without preservation");
        }

        var reloadComplete = new TaskCompletionSource();
        void OnLoaded(ContentLoadedReason reason)
        {
            if (reason == ContentLoadedReason.ExternalReload)
            {
                reloadComplete.TrySetResult();
            }
        }
        MonacoEditor.ContentLoaded += OnLoaded;

        try
        {
            MonacoEditor.NotifyExternalChange();

            var completed = await Task.WhenAny(reloadComplete.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            if (completed != reloadComplete.Task)
            {
                return;
            }

            if (!string.IsNullOrEmpty(savedState))
            {
                await RestoreEditorStateAsync(savedState);
            }
        }
        finally
        {
            MonacoEditor.ContentLoaded -= OnLoaded;
        }
    }

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
}
