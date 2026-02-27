using Celbridge.Documents.Views;

namespace Celbridge.Documents.Services;

/// <summary>
/// Factory for creating spreadsheet document views.
/// Handles Excel files using SpreadJS when available.
/// </summary>
public class SpreadsheetEditorFactory : IDocumentEditorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly FileTypeHelper _fileTypeHelper;

    public IReadOnlyList<string> SupportedExtensions { get; } = new List<string> { ".xlsx" };

    public int Priority => 0;

    public SpreadsheetEditorFactory(IServiceProvider serviceProvider, FileTypeHelper fileTypeHelper)
    {
        _serviceProvider = serviceProvider;
        _fileTypeHelper = fileTypeHelper;
    }

    public bool CanHandle(ResourceKey fileResource, string filePath)
    {
        // Only handle if SpreadJS is available
        return _fileTypeHelper.IsSpreadJSAvailable;
    }

    public Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
#if WINDOWS
        if (!_fileTypeHelper.IsSpreadJSAvailable)
        {
            return Result<IDocumentView>.Fail("SpreadJS license is not available");
        }

        var view = _serviceProvider.GetRequiredService<SpreadsheetDocumentView>();
        return Result<IDocumentView>.Ok(view);
#else
        return Result<IDocumentView>.Fail("Spreadsheet editor is not available on this platform");
#endif
    }
}
