using Celbridge.Documents.ViewModels;

namespace Celbridge.Documents.Views;

/// <summary>
/// This control contains a Monaco editor for editing text documents and an optional preview pane.
/// It acts as a facade for the MonacoEditor control, forwarding on all the IDocumentView interface methods.
/// </summary>
public sealed partial class TextEditorDocumentView : UserControl, IDocumentView
{
    public TextEditorDocumentViewModel ViewModel { get; }

    public bool HasUnsavedChanges => MonacoEditor.HasUnsavedChanges;

    private IPreviewProvider? _previewProvider;

    private bool _supportsPreview;

    public TextEditorDocumentView()
    {
        this.InitializeComponent();

        ViewModel = ServiceLocator.AcquireService<TextEditorDocumentViewModel>();

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        ViewModel.OnSetContent += ViewModel_OnSetContent;
    }

    public async Task<Result> SetFileResource(ResourceKey fileResource)
    {
        // This method can get called multiple types if the document is renamed, so we
        // need to acquire the provider again each time.

        ViewModel.SetFileResource(fileResource);

        var getResult = ViewModel.GetPreviewProvider();
        if (getResult.IsSuccess)
        {
            _supportsPreview = true;
            _previewProvider = getResult.Value;

            if (!MonacoEditor.ViewModel.CachedText.IsNullOrEmpty())
            {
                // If the editor has already been populated (i.e. a file rename from .txt to .md) then
                // we need to update the new preview provider immediately to reflect the cached text.
                await UpdatePreview();
            }
        }
        else
        {
            _supportsPreview = false;
            _previewProvider = null;
        }

        UpdatePanelVisibility();

        return await MonacoEditor.SetFileResource(fileResource);
    }

    public async Task<Result> LoadContent()
    {
        var loadResult = await MonacoEditor.LoadContent();
        if (loadResult.IsSuccess)
        {
            _ = UpdatePreview();
        }

        return loadResult;
    }

    public Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return MonacoEditor.UpdateSaveTimer(deltaTime);
    }

    public async Task<Result> SaveDocument()
    {
        var saveResult = await MonacoEditor.SaveDocument();
        if (saveResult.IsSuccess)
        {
            _ = UpdatePreview();
        }

        return saveResult;
    }

    public async Task<bool> CanClose()
    {
        return await MonacoEditor.CanClose();
    }

    public void PrepareToClose()
    {
        MonacoEditor.PrepareToClose();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.ShowPreview) ||
            e.PropertyName == nameof(ViewModel.ShowEditor))
        {
            UpdatePanelVisibility();
        }
    }

    private void ViewModel_OnSetContent(string content)
    {
        if (MonacoEditor.ViewModel.CachedText == content)
        {
            // The current content already matches the new content, no need to update it.
            return;
        }

        MonacoEditor.SetContent(content);
        _ = UpdatePreview();
    }

    private async Task UpdatePreview()
    {
        if (_previewProvider == null)
        {
            return;
        }

        // Ensure the EditorPreview has the same file path as the Monaco editor
        // This is used to set the virtual hose name for resolving relative links.
        EditorPreview.ViewModel.FilePath = MonacoEditor.ViewModel.FilePath;

        var cachedText = MonacoEditor.ViewModel.CachedText;
        var generateResult = await _previewProvider.GeneratePreview(cachedText, EditorPreview);
        if (generateResult.IsSuccess)
        {
            var generatedHtml = generateResult.Value;
            EditorPreview.ViewModel.PreviewHTML = generatedHtml;
        }
    }

    private void UpdatePanelVisibility()
    {
        // Default to no preview available
        bool isEditorVisible = true;
        bool isPreviewVisible = false;

        if (_supportsPreview)
        {
            isEditorVisible = ViewModel.ShowEditor;
            isPreviewVisible = ViewModel.ShowPreview;
        }

#if WINDOWS
        LeftColumn.Width = isEditorVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
#else
        // Todo: Using GridUnitType.Star causes an exception in Skia+GTK
        LeftColumn.Width = isEditorVisible ? new GridLength(400) : new GridLength(0);
#endif
        PreviewSplitter.Visibility = isEditorVisible ? Visibility.Visible : Visibility.Collapsed;

#if WINDOWS
        RightColumn.Width = isPreviewVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
#else
        // Todo: Using GridUnitType.Star causes an exception in Skia+GTK
        RightColumn.Width = isPreviewVisible ? new GridLength(400) : new GridLength(0);
#endif
        PreviewSplitter.Visibility = isPreviewVisible ? Visibility.Visible : Visibility.Collapsed;
    }
}
