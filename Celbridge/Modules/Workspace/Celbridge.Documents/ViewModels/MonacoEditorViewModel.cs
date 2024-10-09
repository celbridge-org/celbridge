using Celbridge.Commands;
using Celbridge.Foundation;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class MonacoEditorViewModel : DocumentViewModel
{
    private readonly ICommandService _commandService;
    private readonly IDocumentsService _documentsService;

    // Delay before saving the document after the most recent change
    private const double SaveDelay = 1.0; // Seconds

    [ObservableProperty]
    private double _saveTimer;

    public MonacoEditorViewModel(
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _commandService = commandService;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;
    }

    public async Task<Result<string>> LoadDocument()
    {
        try
        {
            // Read the file contents to initialize the text editor
            var text = await File.ReadAllTextAsync(FilePath);
            return Result<string>.Ok(text);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail(ex, $"Failed to load document file: '{FilePath}'");
        }
    }

    public async Task<Result> SaveDocument(string text)
    {
        // Don't immediately try to save again if the save fails.
        HasUnsavedChanges = false;
        SaveTimer = 0;

        try
        {
            await File.WriteAllTextAsync(FilePath, text);
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"Failed to save document file: '{FilePath}'");
        }

        return Result.Ok();
    }

    public Result<bool> UpdateSaveTimer(double deltaTime)
    {
        if (!HasUnsavedChanges)
        {
            return Result<bool>.Fail($"Document does not have unsaved changes: {FileResource}");
        }

        if (SaveTimer > 0)
        {
            SaveTimer -= deltaTime;
            if (SaveTimer <= 0)
            {
                SaveTimer = 0;
                return Result<bool>.Ok(true);
            }
        }

        return Result<bool>.Ok(false);
    }

    public string GetDocumentLanguage()
    {
        return _documentsService.GetDocumentLanguage(FileResource);
    }

    public void OnTextChanged()
    {
        HasUnsavedChanges = true;
        SaveTimer = SaveDelay;
    }

    public void ToggleFocusMode()
    {
        _commandService.Execute<IToggleFocusModeCommand>();
    }
}
