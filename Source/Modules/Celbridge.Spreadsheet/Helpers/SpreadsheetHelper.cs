using Celbridge.Resources;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Helpers;

internal static class SpreadsheetHelper
{
    private const string XlsxExtension = ".xlsx";

    // Saves with EvaluateFormulasBeforeSaving so consumers reading cached
    // values (headless readers, SpreadJS on reload) see fresh results without a
    // separate recalc step. Per-cell evaluation failures skip the cached value
    // for that cell but the file still saves.
    public static void RecalculateInto(XLWorkbook workbook, Stream destination)
    {
        var saveOptions = new SaveOptions
        {
            EvaluateFormulasBeforeSaving = true
        };
        workbook.SaveAs(destination, saveOptions);
    }

    // ClosedXML serialises doubles with 15-digit precision, which rounds
    // values within ~8 orders of magnitude of Double.MaxValue up to a string
    // that overflows to Infinity on reopen, leaving an unrecoverable .xlsx.
    // Reject literal writes of values in that danger zone (and obviously
    // non-finite values) with a clear error rather than silently corrupting
    // the workbook.
    public const double SafeMagnitudeLimit = 1e+300;

    public static Result ValidateNumericValue(double value)
    {
        if (!double.IsFinite(value))
        {
            return Result.Fail($"Numeric value must be finite, was {value}.");
        }

        if (Math.Abs(value) > SafeMagnitudeLimit)
        {
            return Result.Fail($"Numeric value magnitude {value} exceeds the safe range (±{SafeMagnitudeLimit:G2}). ClosedXML rounds values larger than this to a string that overflows on reopen and corrupts the workbook.");
        }

        return Result.Ok();
    }

    public static bool IsColumnRange(string range)
    {
        return range.Split(':').All(part => !string.IsNullOrEmpty(part) && part.All(char.IsLetter));
    }

    public static bool IsRowRange(string range)
    {
        return range.Split(':').All(part => !string.IsNullOrEmpty(part) && part.All(char.IsDigit));
    }

    /// <summary>
    /// Validates that the resource key is a non-empty .xlsx file that exists
    /// inside a registered root. Returns the key on success so callers can
    /// pass it to subsequent chokepoint operations.
    /// </summary>
    public static async Task<Result<ResourceKey>> ResolveWorkbookResourceAsync(
        IWorkspaceWrapper workspaceWrapper,
        ResourceKey fileResource)
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

        var fileStorage = workspaceWrapper.WorkspaceService.FileStorage;
        var infoResult = await fileStorage.GetInfoAsync(fileResource);
        if (infoResult.IsFailure)
        {
            return Result.Fail($"Failed to inspect workbook: '{fileResource}'")
                .WithErrors(infoResult);
        }

        var info = infoResult.Value;
        if (info.Kind == StorageItemKind.NotFound)
        {
            return Result.Fail($"Workbook file not found: '{fileResource}'");
        }
        if (info.Kind != StorageItemKind.File)
        {
            return Result.Fail($"Resource is not a file: '{fileResource}'");
        }

        return fileResource;
    }

    /// <summary>
    /// Loads the workbook bytes via the chokepoint and constructs an XLWorkbook
    /// from an in-memory copy. The caller owns the returned workbook and must
    /// dispose it; the underlying stream is owned by the workbook.
    /// </summary>
    public static async Task<Result<XLWorkbook>> LoadWorkbookAsync(
        IFileStorage fileStorage,
        ResourceKey fileResource)
    {
        var bytesResult = await fileStorage.ReadAllBytesAsync(fileResource);
        if (bytesResult.IsFailure)
        {
            return Result.Fail($"Failed to read workbook: '{fileResource}'")
                .WithErrors(bytesResult);
        }

        try
        {
            // The workbook holds onto the stream; do not dispose the
            // MemoryStream here. ClosedXML closes it when the workbook is
            // disposed.
            var stream = new MemoryStream(bytesResult.Value, writable: false);
            return new XLWorkbook(stream);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to open workbook: '{fileResource}'")
                .WithException(ex);
        }
    }

    /// <summary>
    /// Serialises the workbook to memory and writes it via the chokepoint.
    /// Evaluates formulas before saving so cached values stay fresh.
    /// </summary>
    public static async Task<Result> SaveWorkbookAsync(
        IFileStorage fileStorage,
        ResourceKey fileResource,
        XLWorkbook workbook)
    {
        byte[] bytes;
        try
        {
            using var buffer = new MemoryStream();
            RecalculateInto(workbook, buffer);
            bytes = buffer.ToArray();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to serialise workbook: '{fileResource}'")
                .WithException(ex);
        }

        var writeResult = await fileStorage.WriteAllBytesAsync(fileResource, bytes);
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to save workbook: '{fileResource}'")
                .WithErrors(writeResult);
        }

        return Result.Ok();
    }
}
