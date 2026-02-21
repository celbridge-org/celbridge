using Celbridge.Documents.ViewModels;
using Celbridge.Logging;

namespace Celbridge.Documents.Views;

/// <summary>
/// This control contains a Monaco editor for editing text documents.
/// It acts as a facade for the MonacoEditor control, forwarding on all the IDocumentView interface methods.
/// </summary>
public sealed partial class TextEditorDocumentView : UserControl, IDocumentView
{
    private readonly ILogger<TextEditorDocumentView> _logger;

    public TextEditorDocumentViewModel ViewModel { get; }

    public bool HasUnsavedChanges => MonacoEditor.HasUnsavedChanges;

    public TextEditorDocumentView()
    {
        this.InitializeComponent();

        ViewModel = ServiceLocator.AcquireService<TextEditorDocumentViewModel>();
        _logger = ServiceLocator.AcquireService<ILogger<TextEditorDocumentView>>();

        ViewModel.OnSetContent += ViewModel_OnSetContent;
    }

    public async Task<Result> SetFileResource(ResourceKey fileResource)
    {
        ViewModel.SetFileResource(fileResource);

        return await MonacoEditor.SetFileResource(fileResource);
    }

    public async Task<Result> LoadContent()
    {
        return await MonacoEditor.LoadContent();
    }

    public Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return MonacoEditor.UpdateSaveTimer(deltaTime);
    }

    public async Task<Result> SaveDocument()
    {
        return await MonacoEditor.SaveDocument();
    }

    public async Task<Result> NavigateToLocation(string location)
    {
        return await MonacoEditor.NavigateToLocation(location);
    }

    public async Task<bool> CanClose()
    {
        return await MonacoEditor.CanClose();
    }

    public async Task PrepareToClose()
    {
        async void CloseEditorViews()
        {
            try
            {
                await MonacoEditor.PrepareToClose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while preparing TextEditorDocumentView to close");
            }
        }

        // Quick fire-and-forget call to avoid blocking the UI thread.
        CloseEditorViews();

        await Task.CompletedTask;
    }

    private void ViewModel_OnSetContent(string content)
    {
        if (MonacoEditor.ViewModel.CachedText == content)
        {
            // The current content already matches the new content, no need to update it.
            return;
        }

        MonacoEditor.SetContent(content);
    }
}
