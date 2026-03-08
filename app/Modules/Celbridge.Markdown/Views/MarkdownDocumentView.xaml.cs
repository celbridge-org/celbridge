using System.Text.Json;
using Celbridge.Code.Views;
using Celbridge.Documents.Views;
using Celbridge.Logging;
using Celbridge.Markdown.Services;
using Celbridge.Markdown.ViewModels;
using Celbridge.Messaging;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

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
    private readonly IStringLocalizer _stringLocalizer;

    private string ViewModePreviewTooltip => _stringLocalizer.GetString("Markdown_ViewMode_Preview");
    private string ViewModeSplitTooltip => _stringLocalizer.GetString("Markdown_ViewMode_Split");
    private string ViewModeSourceTooltip => _stringLocalizer.GetString("Markdown_ViewMode_Source");

    private string ViewModePreviewLabel => _stringLocalizer.GetString("Markdown_ViewMode_Preview_Label");
    private string ViewModeSplitLabel => _stringLocalizer.GetString("Markdown_ViewMode_Split_Label");
    private string ViewModeSourceLabel => _stringLocalizer.GetString("Markdown_ViewMode_Source_Label");

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
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        ViewModel = ServiceLocator.AcquireService<MarkdownDocumentViewModel>();

        // Set up content loader callback for Monaco to pull content when needed
        SplitEditor.MonacoEditor.ContentLoader = LoadContentFromDiskAsync;

        // Subscribe to SplitEditorControl events
        SplitEditor.ContentChanged += OnContentChanged;
        SplitEditor.EditorFocused += OnEditorFocused;

        // Subscribe to ViewModel events
        ViewModel.ReloadRequested += OnViewModelReloadRequested;
        ViewModel.ViewModeChanged += OnViewModeChanged;
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

        if (initResult.IsFailure)
        {
            return initResult;
        }

        // Apply the initial view mode after the editor is ready
        ApplyViewMode(MarkdownViewMode.Preview);
        SplitEditor.SetViewMode(SplitEditorViewMode.Preview);

        return Result.Ok();
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

    public override async Task<Result> NavigateToLocation(string location)
    {
        if (string.IsNullOrEmpty(location))
        {
            return Result.Ok();
        }

        try
        {
            using var doc = JsonDocument.Parse(location);
            var root = doc.RootElement;

            var lineNumber = root.TryGetProperty("lineNumber", out var lineProp) ? lineProp.GetInt32() : 1;
            var column = root.TryGetProperty("column", out var colProp) ? colProp.GetInt32() : 1;
            var endLineNumber = root.TryGetProperty("endLineNumber", out var endLineProp) ? endLineProp.GetInt32() : 0;
            var endColumn = root.TryGetProperty("endColumn", out var endColProp) ? endColProp.GetInt32() : 0;

            // Switch to Split mode when navigating from search results in Preview mode so
            // the user can see the exact text selection alongside the rendered preview.
            if (ViewModel.ViewMode == MarkdownViewMode.Preview)
            {
                ApplyViewMode(MarkdownViewMode.Split);
            }

            return await SplitEditor.MonacoEditor.NavigateToLocationAsync(lineNumber, column, endLineNumber, endColumn);
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
            SplitEditor.ContentChanged -= OnContentChanged;
            SplitEditor.EditorFocused -= OnEditorFocused;
            SplitEditor.MonacoEditor.ContentLoader = null;
            ViewModel.ReloadRequested -= OnViewModelReloadRequested;
            ViewModel.ViewModeChanged -= OnViewModeChanged;

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

    private void PreviewModeButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyViewMode(MarkdownViewMode.Preview);
    }

    private void SplitModeButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyViewMode(MarkdownViewMode.Split);
    }

    private void SourceModeButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyViewMode(MarkdownViewMode.Source);
    }

    private void ApplyViewMode(MarkdownViewMode viewMode)
    {
        PreviewModeButton.IsChecked = viewMode == MarkdownViewMode.Preview;
        SplitModeButton.IsChecked = viewMode == MarkdownViewMode.Split;
        SourceModeButton.IsChecked = viewMode == MarkdownViewMode.Source;

        ViewModeLabel.Text = viewMode switch
        {
            MarkdownViewMode.Preview => ViewModePreviewLabel,
            MarkdownViewMode.Split => ViewModeSplitLabel,
            MarkdownViewMode.Source => ViewModeSourceLabel,
            _ => ViewModePreviewLabel
        };

        ViewModel.ViewMode = viewMode;
    }

    private void OnViewModeChanged(object? sender, MarkdownViewMode viewMode)
    {
        var splitEditorViewMode = viewMode switch
        {
            MarkdownViewMode.Preview => SplitEditorViewMode.Preview,
            MarkdownViewMode.Split => SplitEditorViewMode.Split,
            MarkdownViewMode.Source => SplitEditorViewMode.Source,
            _ => SplitEditorViewMode.Preview
        };
        SplitEditor.SetViewMode(splitEditorViewMode);
    }
}
