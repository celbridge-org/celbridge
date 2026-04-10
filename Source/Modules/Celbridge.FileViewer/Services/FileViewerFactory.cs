using Celbridge.Documents;
using Celbridge.Documents.Services;
using Celbridge.Documents.Views;
using Celbridge.FileViewer.Views;

namespace Celbridge.FileViewer.Services;

/// <summary>
/// Factory for creating file viewer document views.
/// Handles binary files like images, audio, video, and PDFs.
/// </summary>
public class FileViewerFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly FileTypeHelper _fileTypeHelper;
    private readonly IReadOnlyList<string> _supportedExtensions;

    public override IReadOnlyList<string> SupportedExtensions => _supportedExtensions;

    public FileViewerFactory(IServiceProvider serviceProvider, FileTypeHelper fileTypeHelper)
    {
        _serviceProvider = serviceProvider;
        _fileTypeHelper = fileTypeHelper;

        // File viewer supported extensions
        var extensions = new List<string>
        {
            // Images
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

        _supportedExtensions = extensions.AsReadOnly();
    }

    public override bool CanHandle(ResourceKey fileResource, string filePath)
    {
        var extension = Path.GetExtension(fileResource.ToString()).ToLowerInvariant();
        return _fileTypeHelper.IsWebViewerFile(extension);
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
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
