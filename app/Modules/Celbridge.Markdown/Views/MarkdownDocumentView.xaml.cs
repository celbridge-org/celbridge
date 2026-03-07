using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Markdown.Services;
using Celbridge.Markdown.ViewModels;
using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.Markdown.Views;

/// <summary>
/// Document view for editing markdown files using the Monaco editor with optional preview.
/// Uses SplitEditorControl for the editor+preview functionality.
/// </summary>
public sealed partial class MarkdownDocumentView : DocumentView
{
    private readonly ILogger<MarkdownDocumentView> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IResourceRegistry _resourceRegistry;

    private readonly MarkdownPreviewRenderer _previewRenderer = new();

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
        SplitEditor.MonacoEditor.ContentLoader = LoadContentFromDiskAsync;

        // Subscribe to SplitEditorControl events
        SplitEditor.ContentChanged += OnContentChanged;
        SplitEditor.EditorFocused += OnEditorFocused;

        // Subscribe to ViewModel events
        ViewModel.ReloadRequested += OnViewModelReloadRequested;
        ViewModel.PreviewVisibilityChanged += OnPreviewVisibilityChanged;
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

        // Configure the preview renderer with document context
        SplitEditor.ConfigurePreview(
            _previewRenderer,
            _resourceRegistry.ProjectFolderPath,
            filePath);

        return await Task.FromResult(Result.Ok());
    }

    public override async Task<Result> LoadContent()
    {
        var loadResult = await ViewModel.LoadDocument();
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to load content for resource: {ViewModel.FileResource}")
                .WithErrors(loadResult);
        }

        var content = loadResult.Value;

        var initResult = await SplitEditor.MonacoEditor.InitializeAsync(
            content,
            "markdown",
            ViewModel.FilePath,
            ViewModel.FileResource.ToString());

        return initResult;
    }

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
            var content = await SplitEditor.GetContentAsync();
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
            SplitEditor.ContentChanged -= OnContentChanged;
            SplitEditor.EditorFocused -= OnEditorFocused;
            SplitEditor.MonacoEditor.ContentLoader = null;
            ViewModel.ReloadRequested -= OnViewModelReloadRequested;
            ViewModel.PreviewVisibilityChanged -= OnPreviewVisibilityChanged;

            ViewModel.Cleanup();

            await SplitEditor.CleanupAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while preparing MarkdownDocumentView to close");
        }
    }

    private void OnContentChanged()
    {
        ViewModel.OnTextChanged();
    }

    private void OnEditorFocused()
    {
        var message = new DocumentViewFocusedMessage(ViewModel.FileResource);
        _messengerService.Send(message);
    }

    private void OnViewModelReloadRequested(object? sender, EventArgs e)
    {
        SplitEditor.MonacoEditor.NotifyExternalChange();
    }

    private void PreviewToggleButton_Click(object sender, RoutedEventArgs e)
    {
        SplitEditor.SetPreviewVisible(ViewModel.IsPreviewVisible);
    }

    private void OnPreviewVisibilityChanged(object? sender, bool isVisible)
    {
        SplitEditor.SetPreviewVisible(isVisible);
    }
}
