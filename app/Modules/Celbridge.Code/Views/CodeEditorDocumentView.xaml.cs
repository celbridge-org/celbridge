using Celbridge.Documents;
using Celbridge.Logging;

namespace Celbridge.Code.Views;

/// <summary>
/// Document view for editing code/text files using the Monaco editor.
/// Delegates to MonacoEditorControl for the actual editing functionality.
/// </summary>
public sealed partial class CodeEditorDocumentView : UserControl, IDocumentView
{
    private readonly ILogger<CodeEditorDocumentView> _logger;

    public bool HasUnsavedChanges => MonacoEditor.HasUnsavedChanges;

    public CodeEditorDocumentView()
    {
        this.InitializeComponent();

        _logger = ServiceLocator.AcquireService<ILogger<CodeEditorDocumentView>>();
    }

    public async Task<Result> SetFileResource(ResourceKey fileResource)
    {
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
        try
        {
            await MonacoEditor.PrepareToClose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while preparing CodeEditorDocumentView to close");
        }
    }
}
