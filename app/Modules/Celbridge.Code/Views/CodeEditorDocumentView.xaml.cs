using System.Text.Json;
using Celbridge.Code.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.Code.Views;

/// <summary>
/// Document view for editing code/text files using a code editor.
/// Handles file I/O and document management, delegating text editing to CodeEditor.
/// </summary>
public sealed partial class CodeEditorDocumentView : DocumentView
{
    private readonly ILogger<CodeEditorDocumentView> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IDocumentsService _documentsService;

    private readonly CodeEditorViewModel _viewModel;

    public override ResourceKey FileResource => _viewModel.FileResource;

    public override bool HasUnsavedChanges => _viewModel.HasUnsavedChanges;

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
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;

        _viewModel = ServiceLocator.AcquireService<CodeEditorViewModel>();

        // Set up content loader callback for the editor to pull content when needed
        MonacoEditor.ContentLoader = LoadContentFromDiskAsync;

        // Subscribe to CodeEditor events
        MonacoEditor.ContentChanged += OnMonacoContentChanged;
        MonacoEditor.EditorFocused += OnMonacoEditorFocused;

        // Subscribe to ViewModel reload requests (external file changes)
        _viewModel.ReloadRequested += OnViewModelReloadRequested;
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

        _viewModel.FileResource = fileResource;
        _viewModel.FilePath = filePath;

        return await Task.FromResult(Result.Ok());
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

    public override async Task<Result> SaveDocument()
    {
        try
        {
            // Get current content from the editor
            var content = await MonacoEditor.GetContentAsync();

            // Save via ViewModel
            return await _viewModel.SaveDocument(content);
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

            return await MonacoEditor.NavigateToLocationAsync(lineNumber, column, endLineNumber, endColumn);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to navigate to location: {location}")
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
            MonacoEditor.ContentLoader = null;
            _viewModel.ReloadRequested -= OnViewModelReloadRequested;

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

    private void OnMonacoContentChanged()
    {
        // Mark document as having unsaved changes
        _viewModel.OnTextChanged();
    }

    private void OnMonacoEditorFocused()
    {
        // Notify the system that this document view has focus
        var message = new DocumentViewFocusedMessage(_viewModel.FileResource);
        _messengerService.Send(message);
    }

    private void OnViewModelReloadRequested(object? sender, EventArgs e)
    {
        // External file change detected - notify the editor to reload
        MonacoEditor.NotifyExternalChange();
    }
}
