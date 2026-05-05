using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

/// <summary>
/// Shared helpers for the ISpreadsheet*Command implementations. Resolves a
/// resource key to an absolute filesystem path and validates that the file
/// exists and is an .xlsx workbook.
/// </summary>
internal static class SpreadsheetCommandHelpers
{
    private const string XlsxExtension = ".xlsx";

    // Saves the workbook with EvaluateFormulasBeforeSaving so that consumers
    // reading cached values (headless xlsx readers, the SpreadJS editor on
    // reload) see fresh results without having to call a separate recalc step.
    // ClosedXML evaluates each formula during save and writes the calculated
    // value alongside the formula. Per ClosedXML's contract, evaluation
    // failures on individual cells (unsupported Excel functions) skip the
    // cached value for that cell but the file still saves.
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
            return Result<string>.Fail("Workbook resource key is required.");
        }

        var extension = Path.GetExtension(fileResource.ToString());
        if (!string.Equals(extension, XlsxExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Result<string>.Fail($"Resource is not an .xlsx workbook: '{fileResource}'");
        }

        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resolveResult = resourceRegistry.ResolveResourcePath(fileResource);
        if (resolveResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to resolve path for resource: '{fileResource}'")
                .WithErrors(resolveResult);
        }
        var workbookPath = resolveResult.Value;

        if (!File.Exists(workbookPath))
        {
            return Result<string>.Fail($"Workbook file not found: '{fileResource}'");
        }

        return Result<string>.Ok(workbookPath);
    }
}
