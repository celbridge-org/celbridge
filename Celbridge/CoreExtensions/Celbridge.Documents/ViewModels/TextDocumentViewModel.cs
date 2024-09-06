using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class TextDocumentViewModel : DocumentViewModel
{
    private readonly IDocumentsService _documentsService;

    // Delay before saving the document after the most recent change
    private const double SaveDelay = 1.0; // Seconds

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private double _saveTimer;

    public TextDocumentViewModel(IWorkspaceWrapper workspaceWrapper)
    {
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;
    }

    public async Task<Result> LoadDocument()
    {
        try
        {
            PropertyChanged -= TextDocumentViewModel_PropertyChanged;

            // Read the file contents to initialize the text editor
            var text = await File.ReadAllTextAsync(FilePath);
            Text = text;

            FileResource = FileResource;
            FilePath = FilePath;

            PropertyChanged += TextDocumentViewModel_PropertyChanged;
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"Failed to load document file: '{FilePath}'");
        }

        return Result.Ok();
    }

    public async Task<Result> SaveDocument()
    {
        // Don't immediately try to save again if the save fails.
        HasUnsavedChanges = false;
        SaveTimer = 0;

        try
        {
            await File.WriteAllTextAsync(FilePath, Text);
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

    public string GetLanguage()
    {
        var extension = System.IO.Path.GetExtension(FilePath).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension))
        {
            return string.Empty;
        }

        return _documentsService.GetDocumentLanguage(extension);
    }

    private void TextDocumentViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Text))
        {
            OnTextChanged();
        }
    }

    public void OnTextChanged()
    {
        HasUnsavedChanges = true;
        SaveTimer = SaveDelay;
    }
}
