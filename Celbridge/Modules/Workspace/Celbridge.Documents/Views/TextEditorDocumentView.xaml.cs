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
        return MonacoEditor.SetFileResource(fileResource);
    }

    public Task<Result> LoadContent()
    {
        return MonacoEditor.LoadContent();
    }

    public Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return MonacoEditor.UpdateSaveTimer(deltaTime);
    }

    public Task<Result> SaveDocument()
    {
        return MonacoEditor.SaveDocument();
    }

    public Task<bool> CanClose()
    {
        return MonacoEditor.CanClose();
    }

    public void PrepareToClose()
    {
        MonacoEditor.PrepareToClose();
    }
}
