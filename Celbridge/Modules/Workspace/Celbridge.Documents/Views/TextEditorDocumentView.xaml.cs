using Celbridge.Documents.ViewModels;
using Celbridge.ExtensionAPI;

namespace Celbridge.Documents.Views;

/// <summary>
/// This control contains a Monaco editor for editing text documents and an optional preview pane.
/// It acts as a facade for the MonacoEditor control, forwarding on all the IDocumentView interface methods.
/// </summary>
public sealed partial class TextEditorDocumentView : UserControl, IDocumentView
{
    public TextEditorDocumentViewModel ViewModel { get; }

    public bool HasUnsavedChanges => MonacoEditor.HasUnsavedChanges;

    private PreviewProvider? _previewProvider;

    public TextEditorDocumentView()
    {
        this.InitializeComponent();

        var serviceProvider = ServiceLocator.ServiceProvider;
        ViewModel = serviceProvider.GetRequiredService<TextEditorDocumentViewModel>();

        SetPreviewVisibility(false);
    }

    private void SetPreviewVisibility(bool isVisible)
    {
#if WINDOWS
        RightColumn.Width = isVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
#else
        // Todo: Using GridUnitType.Star causes an exception in Skia+GTK
        RightColumn.Width = isVisible ? new GridLength(400) : new GridLength(0);
#endif
        PreviewSplitter.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public Task<Result> SetFileResource(ResourceKey fileResource)
    {
        // This method can get called multiple types if the document is renamed, so we
        // acquire the provider again each time.
        _previewProvider = null;
        var getResult = ViewModel.GetPreviewProvider(fileResource);
        if (getResult.IsSuccess)
        {
            SetPreviewVisibility(true);
            _previewProvider = getResult.Value;
        }

        return MonacoEditor.SetFileResource(fileResource);
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

    private async Task UpdatePreview()
    {
        if (_previewProvider == null)
        {
            return;
        }

        var cachedText = MonacoEditor.ViewModel.CachedText;
        var generateResult = await _previewProvider.GeneratePreview(cachedText);
        if (generateResult.IsSuccess)
        {
            var generatedHtml = generateResult.Value;
            EditorPreview.ViewModel.PreviewHTML = generatedHtml;
        }
    }
}
