using System.Text.Json;
using Celbridge.Code.Views;
using Celbridge.Documents.ViewModels;
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
/// Uses SplitCodeEditor for the editor+preview functionality.
/// </summary>
public sealed partial class MarkdownDocumentView : DocumentView
{
    private readonly ILogger<MarkdownDocumentView> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;

    private string ViewModePreviewTooltip => _stringLocalizer.GetString("Markdown_ViewMode_Preview");
    private string ViewModeSplitTooltip => _stringLocalizer.GetString("Markdown_ViewMode_Split");
    private string ViewModeSourceTooltip => _stringLocalizer.GetString("Markdown_ViewMode_Source");

    private string InsertSnippetTooltip => _stringLocalizer.GetString("Markdown_Snippet_Insert");
    private string SnippetBoldLabel => _stringLocalizer.GetString("Markdown_Snippet_Bold");
    private string SnippetItalicLabel => _stringLocalizer.GetString("Markdown_Snippet_Italic");
    private string SnippetStrikethroughLabel => _stringLocalizer.GetString("Markdown_Snippet_Strikethrough");
    private string SnippetUnorderedListLabel => _stringLocalizer.GetString("Markdown_Snippet_UnorderedList");
    private string SnippetOrderedListLabel => _stringLocalizer.GetString("Markdown_Snippet_OrderedList");
    private string SnippetTaskListLabel => _stringLocalizer.GetString("Markdown_Snippet_TaskList");
    private string SnippetCodeBlockLabel => _stringLocalizer.GetString("Markdown_Snippet_CodeBlock");
    private string SnippetBlockquoteLabel => _stringLocalizer.GetString("Markdown_Snippet_Blockquote");
    private string SnippetLinkLabel => _stringLocalizer.GetString("Markdown_Snippet_Link");
    private string SnippetImageLabel => _stringLocalizer.GetString("Markdown_Snippet_Image");
    private string SnippetTableLabel => _stringLocalizer.GetString("Markdown_Snippet_Table");
    private string SnippetFootnoteLabel => _stringLocalizer.GetString("Markdown_Snippet_Footnote");
    private string SnippetHorizontalRuleLabel => _stringLocalizer.GetString("Markdown_Snippet_HorizontalRule");

    private readonly MarkdownPreviewRenderer _previewRenderer = new();

    public MarkdownDocumentViewModel ViewModel { get; }

    protected override DocumentViewModel DocumentViewModel => ViewModel;

    public override ResourceKey FileResource => ViewModel.FileResource;

    public override bool HasUnsavedChanges => ViewModel.HasUnsavedChanges;

    public MarkdownDocumentView()
    {
        this.InitializeComponent();

        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();

        _logger = ServiceLocator.AcquireService<ILogger<MarkdownDocumentView>>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        ViewModel = ServiceLocator.AcquireService<MarkdownDocumentViewModel>();

        // Set up content loader callback for Monaco to pull content when needed
        SplitEditor.MonacoEditor.ContentLoader = LoadContentFromDiskAsync;

        // Subscribe to SplitCodeEditor events
        SplitEditor.ContentChanged += OnContentChanged;
        SplitEditor.EditorFocused += OnEditorFocused;

        // Subscribe to ViewModel events
        ViewModel.ReloadRequested += OnViewModelReloadRequested;
        ViewModel.ViewModeChanged += OnViewModeChanged;
    }

    public override async Task<Result> SetFileResource(ResourceKey fileResource)
    {
        var result = await base.SetFileResource(fileResource);
        if (result.IsFailure)
        {
            return result;
        }

        // Configure the preview renderer with document context
        SplitEditor.ConfigurePreview(
            _previewRenderer,
            ResourceRegistry.ProjectFolderPath,
            DocumentViewModel.FilePath);

        return Result.Ok();
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

    protected override async Task<Result> SaveDocumentContentAsync()
    {
        try
        {
            var content = await SplitEditor.GetContentAsync();
            return await ViewModel.SaveDocumentContent(content);
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

    public override async Task<Result> ApplyEditsAsync(IEnumerable<TextEdit> edits)
    {
        try
        {
            // Switch to Split mode when applying edits in Preview mode so
            // the user can see the changes in the source editor.
            if (ViewModel.ViewMode == MarkdownViewMode.Preview)
            {
                ApplyViewMode(MarkdownViewMode.Split);
            }

            await SplitEditor.ApplyEditsAsync(edits);

            // Mark document as having unsaved changes
            ViewModel.OnTextChanged();

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
        InsertSnippetButton.IsEnabled = viewMode != MarkdownViewMode.Preview;

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

    #region Markdown snippet insertion

    private const string BoldSnippet = "**bold text**";
    private const string ItalicSnippet = "*italic text*";
    private const string StrikethroughSnippet = "~~strikethrough text~~";
    private const string UnorderedListSnippet = "- Item 1\n- Item 2\n- Item 3\n";
    private const string OrderedListSnippet = "1. Item 1\n2. Item 2\n3. Item 3\n";
    private const string TaskListSnippet = "- [ ] Task 1\n- [ ] Task 2\n- [x] Completed task\n";
    private const string CodeBlockSnippet = "```language\ncode here\n```\n";
    private const string BlockquoteSnippet = "> Quoted text here\n";
    private const string LinkSnippet = "[title](https://example.com)";
    private const string ImageSnippet = "![alt text](image.png)";
    private const string TableSnippet = "| Header 1 | Header 2 | Header 3 |\n| -------- | -------- | -------- |\n| Cell     | Cell     | Cell     |\n| Cell     | Cell     | Cell     |\n";
    private const string FootnoteSnippet = "Here is a footnote reference.[^1]\n\n[^1]: Footnote text here.\n";
    private const string HorizontalRuleSnippet = "\n---\n";

    private async void InsertBold_Click(object sender, RoutedEventArgs e) => await InsertSnippetAsync(BoldSnippet);
    private async void InsertItalic_Click(object sender, RoutedEventArgs e) => await InsertSnippetAsync(ItalicSnippet);
    private async void InsertStrikethrough_Click(object sender, RoutedEventArgs e) => await InsertSnippetAsync(StrikethroughSnippet);
    private async void InsertUnorderedList_Click(object sender, RoutedEventArgs e) => await InsertSnippetAsync(UnorderedListSnippet);
    private async void InsertOrderedList_Click(object sender, RoutedEventArgs e) => await InsertSnippetAsync(OrderedListSnippet);
    private async void InsertTaskList_Click(object sender, RoutedEventArgs e) => await InsertSnippetAsync(TaskListSnippet);
    private async void InsertCodeBlock_Click(object sender, RoutedEventArgs e) => await InsertSnippetAsync(CodeBlockSnippet);
    private async void InsertBlockquote_Click(object sender, RoutedEventArgs e) => await InsertSnippetAsync(BlockquoteSnippet);
    private async void InsertLink_Click(object sender, RoutedEventArgs e) => await InsertSnippetAsync(LinkSnippet);
    private async void InsertImage_Click(object sender, RoutedEventArgs e) => await InsertSnippetAsync(ImageSnippet);
    private async void InsertTable_Click(object sender, RoutedEventArgs e) => await InsertSnippetAsync(TableSnippet);
    private async void InsertFootnote_Click(object sender, RoutedEventArgs e) => await InsertSnippetAsync(FootnoteSnippet);
    private async void InsertHorizontalRule_Click(object sender, RoutedEventArgs e) => await InsertSnippetAsync(HorizontalRuleSnippet);

    private async Task InsertSnippetAsync(string snippet)
    {
        await SplitEditor.InsertTextAtCaretAsync(snippet);
    }

    #endregion
}
