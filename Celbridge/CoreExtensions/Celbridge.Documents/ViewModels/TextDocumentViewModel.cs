using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class TextDocumentViewModel : DocumentViewModel
{
    // Delay before saving the document after the most recent change
    private const double SaveDelay = 1.0; // Seconds

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private double _saveTimer;

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
        // Todo: Get this lookup table from the Monaco or VSCode projects

        string language;
        var extension = System.IO.Path.GetExtension(FilePath).ToLowerInvariant();

        switch (extension)
        {
            case ".js":
                language = "javascript";
                break;
            case ".ts":
                language = "typescript";
                break;
            case ".json":
                language = "json";
                break;
            case ".html":
            case ".htm":
                language = "html";
                break;
            case ".css":
                language = "css";
                break;
            case ".scss":
                language = "scss";
                break;
            case ".less":
                language = "less";
                break;
            case ".md":
                language = "markdown";
                break;
            case ".py":
                language = "python";
                break;
            case ".java":
                language = "java";
                break;
            case ".c":
                language = "c";
                break;
            case ".cpp":
                language = "cpp";
                break;
            case ".cs":
                language = "csharp";
                break;
            case ".php":
                language = "php";
                break;
            case ".rb":
                language = "ruby";
                break;
            case ".go":
                language = "go";
                break;
            case ".lua":
                language = "lua";
                break;
            case ".xml":
                language = "xml";
                break;
            case ".sql":
                language = "sql";
                break;
            case ".yaml":
            case ".yml":
                language = "yaml";
                break;
            case ".sh":
                language = "shell";
                break;
            // Add more cases as needed
            default:
                language = "plaintext";
                break;
        }

        return language;
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
