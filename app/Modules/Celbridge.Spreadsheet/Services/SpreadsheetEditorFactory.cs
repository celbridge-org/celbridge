using Celbridge.Documents;
using Celbridge.Documents.Services;
using Celbridge.Spreadsheet.Views;

namespace Celbridge.Spreadsheet.Services;

/// <summary>
/// Factory for creating spreadsheet document views.
/// Handles Excel files using SpreadJS when available.
/// </summary>
public class SpreadsheetEditorFactory : DocumentEditorFactoryBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly FileTypeHelper _fileTypeHelper;

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".xlsx"];

    public SpreadsheetEditorFactory(IServiceProvider serviceProvider, FileTypeHelper fileTypeHelper)
    {
        _serviceProvider = serviceProvider;
        _fileTypeHelper = fileTypeHelper;
    }

    public override bool CanHandle(ResourceKey fileResource, string filePath)
    {
        // Only handle if SpreadJS is available AND extension matches
        if (!_fileTypeHelper.IsSpreadJSAvailable)
        {
            return false;
        }

        return base.CanHandle(fileResource, filePath);
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
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
