using System.Text.Json;
using Celbridge.Code.ViewModels;
using Celbridge.Documents;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.Code.Views;

/// <summary>
/// Document view for editing code/text files using the Monaco editor.
/// Handles file I/O and document management, delegating text editing to MonacoEditorControl.
/// </summary>
public sealed partial class CodeEditorDocumentView : UserControl, IDocumentView
{
    private readonly ILogger<CodeEditorDocumentView> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IDocumentsService _documentsService;

    private readonly CodeEditorViewModel _viewModel;

    public bool HasUnsavedChanges => _viewModel.HasUnsavedChanges;

    public CodeEditorDocumentView()
    {
        this.InitializeComponent();

        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();

        _logger = ServiceLocator.AcquireService<ILogger<CodeEditorDocumentView>>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;

        _viewModel = ServiceLocator.AcquireService<CodeEditorViewModel>();

        // Subscribe to MonacoEditorControl events
        MonacoEditor.ContentChanged += OnMonacoContentChanged;
        MonacoEditor.ReloadRequested += OnMonacoReloadRequested;
        MonacoEditor.EditorFocused += OnMonacoEditorFocused;

        // Subscribe to ViewModel reload requests (external file changes)
        _viewModel.ReloadRequested += OnViewModelReloadRequested;
    }

    public async Task<Result> SetFileResource(ResourceKey fileResource)
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

    public async Task<Result> LoadContent()
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

        // Initialize Monaco with the content
        var initResult = await MonacoEditor.InitializeAsync(
            content,
            language,
            _viewModel.FilePath,
            _viewModel.FileResource.ToString());

        return initResult;
    }

    public Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return _viewModel.UpdateSaveTimer(deltaTime);
    }

    public async Task<Result> SaveDocument()
    {
        try
        {
            // Get current content from Monaco
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

    public async Task<Result> NavigateToLocation(string location)
    {
        if (string.IsNullOrEmpty(location))
        {
            return Result.Ok();
        }

        try
        {
            // Parse the location JSON to extract line number and column
            using var doc = JsonDocument.Parse(location);
            var root = doc.RootElement;

            var lineNumber = root.TryGetProperty("lineNumber", out var lineProp) ? lineProp.GetInt32() : 1;
            var column = root.TryGetProperty("column", out var colProp) ? colProp.GetInt32() : 1;

            return await MonacoEditor.NavigateToLocationAsync(lineNumber, column);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to navigate to location: {location}")
                .WithException(ex);
        }
    }

    public async Task<bool> CanClose()
    {
        await Task.CompletedTask;
        return true;
    }

    public async Task PrepareToClose()
    {
        try
        {
            // Unsubscribe from events
            MonacoEditor.ContentChanged -= OnMonacoContentChanged;
            MonacoEditor.ReloadRequested -= OnMonacoReloadRequested;
            MonacoEditor.EditorFocused -= OnMonacoEditorFocused;
            _viewModel.ReloadRequested -= OnViewModelReloadRequested;

            // Cleanup ViewModel
            _viewModel.Cleanup();

            // Cleanup Monaco control
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

    private async void OnMonacoReloadRequested(object? sender, EventArgs e)
    {
        // Monaco requested a reload - load fresh content from disk
        var loadResult = await _viewModel.LoadDocument();
        if (loadResult.IsSuccess)
        {
            MonacoEditor.SetContent(loadResult.Value);
        }
    }

    private void OnMonacoEditorFocused()
    {
        // Notify the system that this document view has focus
        var message = new DocumentViewFocusedMessage(_viewModel.FileResource);
        _messengerService.Send(message);
    }

    private void OnViewModelReloadRequested(object? sender, EventArgs e)
    {
        // External file change detected - notify Monaco to reload
        MonacoEditor.NotifyExternalChange();
    }
}
