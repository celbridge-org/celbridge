using Celbridge.Commands;
using Celbridge.Spreadsheet.Helpers;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class SortRangeCommand : CommandBase, ISortRangeCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Sheet { get; set; } = string.Empty;
    public string Range { get; set; } = string.Empty;
    public IReadOnlyList<SortKey> SortKeys { get; set; } = Array.Empty<SortKey>();
    public bool HasHeaderRow { get; set; }
    public bool MatchCase { get; set; }

    public SortRangeResult ResultValue { get; private set; } =
        new SortRangeResult(0);

    public SortRangeCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var resolveResult = await SpreadsheetHelper.ResolveWorkbookResourceAsync(_workspaceWrapper, FileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail(resolveResult);
        }
        var workbookResource = resolveResult.Value;

        if (string.IsNullOrEmpty(Sheet))
        {
            return Result.Fail("Sheet name is required.");
        }
        if (SortKeys.Count == 0)
        {
            return Result.Fail("At least one sort key is required.");
        }
        if (!string.IsNullOrEmpty(Range)
            && Range.Contains('!'))
        {
            return Result.Fail($"Range '{Range}' must not include a sheet qualifier.");
        }

        var fileStorage = _workspaceWrapper.WorkspaceService.FileStorage;
        var loadResult = await SpreadsheetHelper.LoadWorkbookAsync(fileStorage, workbookResource);
        if (loadResult.IsFailure)
        {
            return Result.Fail(loadResult);
        }

        try
        {
            using var workbook = loadResult.Value;

            if (!workbook.Worksheets.Contains(Sheet))
            {
                return Result.Fail($"Sheet not found: '{Sheet}'.");
            }
            var worksheet = workbook.Worksheet(Sheet);

            var resolveRangeResult = ResolveSortRange(worksheet);
            if (resolveRangeResult.IsFailure)
            {
                return Result.Fail(resolveRangeResult);
            }
            var resolvedRange = resolveRangeResult.Value;
            if (resolvedRange.IsEmpty)
            {
                ResultValue = new SortRangeResult(0);
                return Result.Ok();
            }
            var sortRange = resolvedRange.Range;

            var buildSortStringResult = BuildSortString(sortRange);
            if (buildSortStringResult.IsFailure)
            {
                return Result.Fail(buildSortStringResult);
            }
            var sortString = buildSortStringResult.Value;

            sortRange.Sort(sortString, XLSortOrder.Ascending, MatchCase, ignoreBlanks: true);

            var saveResult = await SpreadsheetHelper.SaveWorkbookAsync(fileStorage, workbookResource, workbook);
            if (saveResult.IsFailure)
            {
                return Result.Fail(saveResult);
            }

            ResultValue = new SortRangeResult(sortRange.RowCount());
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to sort range '{Range}' on '{Sheet}'").WithException(ex);
        }

        return Result.Ok();
    }

    // ResolvedSortRange.IsEmpty is true when there is nothing to sort: an
    // empty workbook with no used range, or a single-row range with the
    // header excluded. In both cases the command succeeds with RowCount 0.
    private record ResolvedSortRange(bool IsEmpty, IXLRange Range);

    private Result<ResolvedSortRange> ResolveSortRange(IXLWorksheet worksheet)
    {
        IXLRange baseRange;
        if (string.IsNullOrEmpty(Range))
        {
            var usedRange = worksheet.RangeUsed();
            if (usedRange is null)
            {
                return new ResolvedSortRange(true, worksheet.Range("A1"));
            }
            baseRange = usedRange;
        }
        else
        {
            try
            {
                baseRange = worksheet.Range(Range);
            }
            catch (Exception ex)
            {
                return Result.Fail($"Invalid range '{Range}': {ex.Message}");
            }
        }

        if (!HasHeaderRow)
        {
            return new ResolvedSortRange(false, baseRange);
        }

        var firstAddress = baseRange.RangeAddress.FirstAddress;
        var lastAddress = baseRange.RangeAddress.LastAddress;
        if (firstAddress.RowNumber == lastAddress.RowNumber)
        {
            // Range is a single row; with hasHeaderRow=true there's nothing
            // below the header to sort.
            return new ResolvedSortRange(true, baseRange);
        }

        var sortRange = worksheet.Range(
            firstAddress.RowNumber + 1,
            firstAddress.ColumnNumber,
            lastAddress.RowNumber,
            lastAddress.ColumnNumber);
        return new ResolvedSortRange(false, sortRange);
    }

    private Result<string> BuildSortString(IXLRange sortRange)
    {
        var firstColumn = sortRange.RangeAddress.FirstAddress.ColumnNumber;
        var lastColumn = sortRange.RangeAddress.LastAddress.ColumnNumber;
        var parts = new List<string>(SortKeys.Count);

        for (int keyIndex = 0; keyIndex < SortKeys.Count; keyIndex++)
        {
            var sortKey = SortKeys[keyIndex];
            if (string.IsNullOrEmpty(sortKey.Column))
            {
                return Result.Fail($"Sort key {keyIndex + 1}: column is required.");
            }

            int absoluteColumnNumber;
            try
            {
                absoluteColumnNumber = ResolveColumnNumber(sortKey.Column);
            }
            catch (Exception ex)
            {
                return Result.Fail($"Sort key {keyIndex + 1}: invalid column '{sortKey.Column}': {ex.Message}");
            }

            if (absoluteColumnNumber < firstColumn
                || absoluteColumnNumber > lastColumn)
            {
                return Result.Fail($"Sort key {keyIndex + 1}: column '{sortKey.Column}' is outside the sort range.");
            }

            int relativeColumnNumber = absoluteColumnNumber - firstColumn + 1;
            string direction = sortKey.Ascending ? "ASC" : "DESC";
            parts.Add($"{relativeColumnNumber} {direction}");
        }

        return string.Join(", ", parts);
    }

    private static int ResolveColumnNumber(string column)
    {
        if (int.TryParse(column, out var columnNumber))
        {
            if (columnNumber < 1)
            {
                throw new ArgumentException("Column number must be at least 1.");
            }
            return columnNumber;
        }
        return XLHelper.GetColumnNumberFromLetter(column);
    }
}
