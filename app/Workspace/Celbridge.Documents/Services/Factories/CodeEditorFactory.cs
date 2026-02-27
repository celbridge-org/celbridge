using Celbridge.Documents.Views;

namespace Celbridge.Documents.Services;

/// <summary>
/// Factory for creating code/text editor document views.
/// Handles text files using the Monaco editor on Windows, or TextBox fallback on other platforms.
/// </summary>
public class CodeEditorFactory : IDocumentEditorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly FileTypeHelper _fileTypeHelper;

    public IReadOnlyList<string> SupportedExtensions { get; }

    public int Priority => 0;

    public CodeEditorFactory(IServiceProvider serviceProvider, FileTypeHelper fileTypeHelper)
    {
        _serviceProvider = serviceProvider;
        _fileTypeHelper = fileTypeHelper;

        // Common text file extensions.
        // The actual complete list comes from TextEditorTypes.json, but we add common ones here
        // to ensure the factory can be matched before falling back to the file type helper.
        var extensions = new List<string>
        {
            ".txt",
            ".cs",
            ".js",
            ".ts",
            ".json",
            ".xml",
            ".html",
            ".css",
            ".py",
            ".yaml",
            ".yml",
            ".toml",
            ".ini",
            ".sh",
            ".bash",
            ".ps1",
            ".bat",
            ".cmd",
            ".sql",
            ".java",
            ".cpp",
            ".c",
            ".h",
            ".hpp",
            ".rs",
            ".go",
            ".swift",
            ".kt",
            ".rb",
            ".php",
            ".r",
            ".lua",
            ".pl",
            ".scala",
            ".m",
            ".mm",
            ".vb",
            ".fs",
            ".fsx",
            ".clj",
            ".erl",
            ".ex",
            ".exs",
            ".hs",
            ".ml",
            ".coffee",
            ".dart",
            ".jl",
            ".dockerfile",
            ".makefile",
            ".cmake",
            ".gradle",
            ".pom",
            ".csproj",
            ".fsproj",
            ".vbproj",
            ".props",
            ".targets",
            ".sln",
            ".log",
            ".env",
            ".gitignore",
            ".editorconfig"
        };

        SupportedExtensions = extensions.AsReadOnly();
    }

    public bool CanHandle(ResourceKey fileResource, string filePath)
    {
        // The file type helper determines if this is actually a text file
        var extension = Path.GetExtension(fileResource.ToString()).ToLowerInvariant();
        var viewType = _fileTypeHelper.GetDocumentViewType(extension);
        return viewType == DocumentViewType.TextDocument;
    }

    public Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
#if WINDOWS
        var view = _serviceProvider.GetRequiredService<TextEditorDocumentView>();
        return Result<IDocumentView>.Ok(view);
#else
        var view = _serviceProvider.GetRequiredService<TextBoxDocumentView>();
        return Result<IDocumentView>.Ok(view);
#endif
    }
}
