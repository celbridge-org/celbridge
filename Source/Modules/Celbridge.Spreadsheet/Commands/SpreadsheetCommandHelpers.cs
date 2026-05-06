using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

internal static class SpreadsheetCommandHelpers
{
    private const string XlsxExtension = ".xlsx";

    // Saves with EvaluateFormulasBeforeSaving so consumers reading cached
    // values (headless readers, SpreadJS on reload) see fresh results without a
    // separate recalc step. Per-cell evaluation failures skip the cached value
    // for that cell but the file still saves.
    public static void RecalculateAndSave(XLWorkbook workbook)
    {
        var saveOptions = new SaveOptions
        {
            EvaluateFormulasBeforeSaving = true
        };
        workbook.Save(saveOptions);
    }

    public static bool IsColumnRange(string range)
    {
        return range.Split(':').All(part => !string.IsNullOrEmpty(part) && part.All(char.IsLetter));
    }

    public static bool IsRowRange(string range)
    {
        return range.Split(':').All(part => !string.IsNullOrEmpty(part) && part.All(char.IsDigit));
    }

    public static Result<string> ResolveWorkbookPath(IWorkspaceWrapper workspaceWrapper, ResourceKey fileResource)
    {
        if (fileResource.IsEmpty)
        {
            return Result.Fail("Workbook resource key is required.");
        }

        var extension = Path.GetExtension(fileResource.ToString());
        if (!string.Equals(extension, XlsxExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail($"Resource is not an .xlsx workbook: '{fileResource}'");
        }

        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resolveResult = resourceRegistry.ResolveResourcePath(fileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{fileResource}'")
                .WithErrors(resolveResult);
        }
        var workbookPath = resolveResult.Value;

        if (!File.Exists(workbookPath))
        {
            return Result.Fail($"Workbook file not found: '{fileResource}'");
        }

        return workbookPath;
    }
}
