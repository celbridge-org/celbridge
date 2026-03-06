using Celbridge.Code.Views;
using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Markdown.ViewModels;
using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.Markdown.Views;

/// <summary>
/// Document view for editing markdown files using the Monaco editor.
/// Provides source-first editing of .md files with syntax highlighting.
/// </summary>
public sealed partial class MarkdownDocumentView : DocumentView
{
    private readonly ILogger<MarkdownDocumentView> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IResourceRegistry _resourceRegistry;

    public MarkdownDocumentViewModel ViewModel { get; }

    public override ResourceKey FileResource => ViewModel.FileResource;

    public override bool HasUnsavedChanges => ViewModel.HasUnsavedChanges;

    public MarkdownDocumentView()
    {
        this.InitializeComponent();

        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();

        _logger = ServiceLocator.AcquireService<ILogger<MarkdownDocumentView>>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        ViewModel = ServiceLocator.AcquireService<MarkdownDocumentViewModel>();

        // Set up content loader callback for Monaco to pull content when needed
        MonacoEditor.ContentLoader = LoadContentFromDiskAsync;

        // Subscribe to MonacoEditorControl events
        MonacoEditor.ContentChanged += OnMonacoContentChanged;
        MonacoEditor.EditorFocused += OnMonacoEditorFocused;

        // Subscribe to ViewModel reload requests (external file changes)
        ViewModel.ReloadRequested += OnViewModelReloadRequested;
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

        return await Task.FromResult(Result.Ok());
    }

    public override async Task<Result> LoadContent()
    {
        // Load file content via ViewModel
        var loadResult = await ViewModel.LoadDocument();
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to load content for resource: {ViewModel.FileResource}")
                .WithErrors(loadResult);
        }

        var content = loadResult.Value;

        // Initialize Monaco with the content, using markdown language
        var initResult = await MonacoEditor.InitializeAsync(
            content,
            "markdown",
            ViewModel.FilePath,
            ViewModel.FileResource.ToString());

        return initResult;
    }

    /// <summary>
    /// Loads content from disk. Used as the ContentLoader callback for Monaco.
    /// </summary>
    private async Task<string> LoadContentFromDiskAsync()
    {
        var loadResult = await ViewModel.LoadDocument();
        if (loadResult.IsFailure)
        {
            _logger.LogError(loadResult, $"Failed to load content for resource: {ViewModel.FileResource}");
            return string.Empty;
        }

        return loadResult.Value;
    }

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return ViewModel.UpdateSaveTimer(deltaTime);
    }

    public override async Task<Result> SaveDocument()
    {
        try
        {
            // Get current content from Monaco
            var content = await MonacoEditor.GetContentAsync();

            // Save via ViewModel
            return await ViewModel.SaveDocument(content);
        }
        catch (Exception ex)
        {
            return Result.Fail("Failed to save document")
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
            ViewModel.ReloadRequested -= OnViewModelReloadRequested;

            // Cleanup ViewModel
            ViewModel.Cleanup();

            // Cleanup Monaco control
            await MonacoEditor.CleanupAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while preparing MarkdownDocumentView to close");
        }
    }

    private void OnMonacoContentChanged()
    {
        // Mark document as having unsaved changes
        ViewModel.OnTextChanged();
    }

    private void OnMonacoEditorFocused()
    {
        // Notify the system that this document view has focus
        var message = new DocumentViewFocusedMessage(ViewModel.FileResource);
        _messengerService.Send(message);
    }

    private void OnViewModelReloadRequested(object? sender, EventArgs e)
    {
        // External file change detected - notify Monaco to reload
        MonacoEditor.NotifyExternalChange();
    }
}
