using Celbridge.Explorer;
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

    public Action? LoadedContent { get; internal set; }

    public async Task<Result> LoadDocument(ResourceKey fileResource, string filePath)
    {
        try
        {
            PropertyChanged -= TextDocumentViewModel_PropertyChanged;

            // Read the file contents to initialize the text editor
            var text = await File.ReadAllTextAsync(filePath);
            Text = text;

            FileResource = fileResource;
            FilePath = filePath;

            LoadedContent?.Invoke();

            PropertyChanged += TextDocumentViewModel_PropertyChanged;
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"Failed to read file contents: '{filePath}'");
        }

        return Result.Ok();
    }

    public Result<bool> UpdateSaveTimer(double deltaTime)
    {
        if (!IsDirty)
        {
            return Result<bool>.Fail($"Document is not dirty: {FileResource}");
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
        IsDirty = true;
        SaveTimer = SaveDelay;
    }
}
