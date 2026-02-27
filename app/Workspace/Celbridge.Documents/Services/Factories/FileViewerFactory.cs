using Celbridge.Documents.Views;

namespace Celbridge.Documents.Services;

/// <summary>
/// Factory for creating file viewer document views.
/// Handles binary files like images, audio, video, and PDFs.
/// </summary>
public class FileViewerFactory : IDocumentEditorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly FileTypeHelper _fileTypeHelper;

    public IReadOnlyList<string> SupportedExtensions { get; }

    public int Priority => 0;

    public FileViewerFactory(IServiceProvider serviceProvider, FileTypeHelper fileTypeHelper)
    {
        _serviceProvider = serviceProvider;
        _fileTypeHelper = fileTypeHelper;

        // File viewer supported extensions
        var extensions = new List<string>
        {
            // Images
            ".png",
            ".jpg",
            ".jpeg",
            ".gif",
            ".webp",
            ".svg",
            ".bmp",
            ".ico",

            // Audio
            ".mp3",
            ".wav",
            ".ogg",
            ".flac",
            ".m4a",

            // Video
            ".mp4",
            ".webm",
            ".avi",
            ".mov",
            ".mkv",

            // Documents
            ".pdf"
        };

        SupportedExtensions = extensions.AsReadOnly();
    }

    public bool CanHandle(ResourceKey fileResource, string filePath)
    {
        var extension = Path.GetExtension(fileResource.ToString()).ToLowerInvariant();
        return _fileTypeHelper.IsWebViewerFile(extension);
    }

    public Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
#if WINDOWS
        var view = _serviceProvider.GetRequiredService<FileViewerDocumentView>();
        return Result<IDocumentView>.Ok(view);
#else
        // On non-Windows platforms, file viewer falls back to text box (limited support)
        var view = _serviceProvider.GetRequiredService<TextBoxDocumentView>();
        return Result<IDocumentView>.Ok(view);
#endif
    }
}
