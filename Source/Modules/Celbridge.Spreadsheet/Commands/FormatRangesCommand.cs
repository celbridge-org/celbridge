using Celbridge.Commands;
using Celbridge.Spreadsheet.Services;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Commands;

public class FormatRangesCommand : CommandBase, ISpreadsheetFormatRangesCommand
{
    private enum BorderPosition { Top, Bottom, Left, Right }

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public IReadOnlyList<SpreadsheetFormatEdit> Edits { get; set; } = Array.Empty<SpreadsheetFormatEdit>();

    public SpreadsheetFormatRangesResult ResultValue { get; private set; } =
        new SpreadsheetFormatRangesResult(0, 0, false);

    public FormatRangesCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        var resolveResult = SpreadsheetCommandHelpers.ResolveWorkbookPath(_workspaceWrapper, FileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail(resolveResult.FirstErrorMessage);
        }
        var workbookPath = resolveResult.Value;

        if (Edits.Count == 0)
        {
            return Result.Fail("At least one format edit is required.");
        }

        for (int editIndex = 0; editIndex < Edits.Count; editIndex++)
        {
            var edit = Edits[editIndex];
            if (string.IsNullOrEmpty(edit.Sheet))
            {
                return Result.Fail($"Edit {editIndex + 1}: sheet name is required.");
            }
            if (string.IsNullOrEmpty(edit.Range))
            {
                return Result.Fail($"Edit {editIndex + 1}: range is required.");
            }
        }

        int totalPropertiesApplied = 0;
        bool anyAutoFitApplied = false;

        try
        {
            using var workbook = new XLWorkbook(workbookPath);

            for (int editIndex = 0; editIndex < Edits.Count; editIndex++)
            {
                var edit = Edits[editIndex];

                if (!workbook.Worksheets.Contains(edit.Sheet))
                {
                    return Result.Fail($"Edit {editIndex + 1}: sheet not found: '{edit.Sheet}'.");
                }
                var worksheet = workbook.Worksheet(edit.Sheet);

                var applyResult = ApplyFormat(worksheet, edit.Range, edit.Format, out int propertiesApplied, out bool autoFitApplied);
                if (applyResult.IsFailure)
                {
                    return Result.Fail($"Edit {editIndex + 1} ('{edit.Sheet}!{edit.Range}'): {applyResult.FirstErrorMessage}");
                }

                totalPropertiesApplied += propertiesApplied;
                if (autoFitApplied)
                {
                    anyAutoFitApplied = true;
                }
            }

            SpreadsheetCommandHelpers.RecalculateAndSave(workbook);

            ResultValue = new SpreadsheetFormatRangesResult(Edits.Count, totalPropertiesApplied, anyAutoFitApplied);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to apply format edits to '{FileResource}'").WithException(ex);
        }

        return Result.Ok();
    }

    private static Result ApplyFormat(
        IXLWorksheet worksheet,
        string range,
        SpreadsheetFormatSpec format,
        out int propertiesApplied,
        out bool autoFitApplied)
    {
        propertiesApplied = 0;
        autoFitApplied = false;

        if (format.MergeRange.HasValue
            && (IsColumnRange(range) || IsRowRange(range)))
        {
            return Result.Fail("mergeRange cannot be applied to a column or row range; use an A1 cell range like 'A1:C3'.");
        }

        if (IsColumnRange(range))
        {
            return ApplyFormatToColumns(worksheet, range, format, out propertiesApplied, out autoFitApplied);
        }

        if (IsRowRange(range))
        {
            return ApplyFormatToRows(worksheet, range, format, out propertiesApplied, out autoFitApplied);
        }

        return ApplyFormatToCellRange(worksheet, range, format, out propertiesApplied, out autoFitApplied);
    }

    private static Result ApplyFormatToColumns(
        IXLWorksheet worksheet,
        string range,
        SpreadsheetFormatSpec format,
        out int propertiesApplied,
        out bool autoFitApplied)
    {
        propertiesApplied = 0;
        autoFitApplied = false;

        List<IXLColumn> columns;
        try
        {
            columns = worksheet.Columns(range).ToList();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Invalid column range '{range}': {ex.Message}");
        }

        var workbook = worksheet.Workbook;
        foreach (var column in columns)
        {
            var styleResult = ApplyFormatToStyle(column.Style, format, workbook);
            if (styleResult.IsFailure)
            {
                return styleResult;
            }
        }
        propertiesApplied += CountStyleProperties(format);

        if (format.ColumnWidth.HasValue)
        {
            var resolvedWidth = ResolveColumnWidth(format.ColumnWidth.Value, workbook);
            foreach (var column in columns)
            {
                column.Width = resolvedWidth;
            }
            propertiesApplied++;
        }

        if (format.AutoFitColumns == true)
        {
            foreach (var column in columns)
            {
                column.AdjustToContents();
            }
            autoFitApplied = true;
        }

        return Result.Ok();
    }

    private static Result ApplyFormatToRows(
        IXLWorksheet worksheet,
        string range,
        SpreadsheetFormatSpec format,
        out int propertiesApplied,
        out bool autoFitApplied)
    {
        propertiesApplied = 0;
        autoFitApplied = false;

        List<IXLRow> rows;
        try
        {
            rows = worksheet.Rows(range).ToList();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Invalid row range '{range}': {ex.Message}");
        }

        var workbook = worksheet.Workbook;
        foreach (var row in rows)
        {
            var styleResult = ApplyFormatToStyle(row.Style, format, workbook);
            if (styleResult.IsFailure)
            {
                return styleResult;
            }
        }
        propertiesApplied += CountStyleProperties(format);

        if (format.RowHeight.HasValue)
        {
            var resolvedHeight = ResolveRowHeight(format.RowHeight.Value, workbook);
            foreach (var row in rows)
            {
                row.Height = resolvedHeight;
            }
            propertiesApplied++;
        }

        return Result.Ok();
    }

    private static Result ApplyFormatToCellRange(
        IXLWorksheet worksheet,
        string range,
        SpreadsheetFormatSpec format,
        out int propertiesApplied,
        out bool autoFitApplied)
    {
        propertiesApplied = 0;
        autoFitApplied = false;

        IXLRange xlRange;
        try
        {
            xlRange = worksheet.Range(range);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Invalid cell range '{range}': {ex.Message}");
        }

        // Apply style cell-by-cell using explicit coordinates so every cell in
        // the range is covered regardless of whether it has been initialised in
        // ClosedXML's in-memory model. IXLRange.Cells() only returns cells that
        // have been accessed before, skipping uninitialized empty cells.
        var firstRow = xlRange.RangeAddress.FirstAddress.RowNumber;
        var lastRow = xlRange.RangeAddress.LastAddress.RowNumber;
        var firstColumn = xlRange.RangeAddress.FirstAddress.ColumnNumber;
        var lastColumn = xlRange.RangeAddress.LastAddress.ColumnNumber;

        var workbook = worksheet.Workbook;
        for (int rowNumber = firstRow; rowNumber <= lastRow; rowNumber++)
        {
            for (int columnNumber = firstColumn; columnNumber <= lastColumn; columnNumber++)
            {
                var cell = worksheet.Cell(rowNumber, columnNumber);
                var styleResult = ApplyFormatToStyle(cell.Style, format, workbook);
                if (styleResult.IsFailure)
                {
                    return styleResult;
                }
            }
        }
        propertiesApplied += CountStyleProperties(format);

        if (format.ColumnWidth.HasValue)
        {
            var resolvedWidth = ResolveColumnWidth(format.ColumnWidth.Value, workbook);
            foreach (var column in xlRange.Columns())
            {
                column.WorksheetColumn().Width = resolvedWidth;
            }
            propertiesApplied++;
        }

        if (format.RowHeight.HasValue)
        {
            var resolvedHeight = ResolveRowHeight(format.RowHeight.Value, workbook);
            foreach (var row in xlRange.Rows())
            {
                row.WorksheetRow().Height = resolvedHeight;
            }
            propertiesApplied++;
        }

        if (format.AutoFitColumns == true)
        {
            foreach (var column in xlRange.Columns())
            {
                column.WorksheetColumn().AdjustToContents();
            }
            autoFitApplied = true;
        }

        if (format.MergeRange == true)
        {
            xlRange.Merge();
            propertiesApplied++;
        }
        else if (format.MergeRange == false)
        {
            if (xlRange.IsMerged())
            {
                xlRange.Unmerge();
            }
            propertiesApplied++;
        }

        return Result.Ok();
    }

    private static Result ApplyFormatToStyle(IXLStyle style, SpreadsheetFormatSpec format, IXLWorkbook workbook)
    {
        if (format.TextFormat is not null)
        {
            var result = ApplyTextFormat(style, format.TextFormat, workbook);
            if (result.IsFailure)
            {
                return result;
            }
        }

        if (format.BackgroundColor is not null)
        {
            if (format.BackgroundColor.Length == 0)
            {
                // Empty string is the explicit "clear fill" sentinel.
                style.Fill.PatternType = XLFillPatternValues.None;
            }
            else
            {
                var colorResult = SpreadsheetFormatConverter.ParseColor(format.BackgroundColor);
                if (colorResult.IsFailure)
                {
                    return colorResult;
                }
                style.Fill.PatternType = XLFillPatternValues.Solid;
                style.Fill.BackgroundColor = colorResult.Value;
            }
        }

        if (format.Borders is not null)
        {
            var result = ApplyBorders(style.Border, format.Borders, workbook);
            if (result.IsFailure)
            {
                return result;
            }
        }

        if (format.HorizontalAlignment is not null)
        {
            var result = SpreadsheetFormatConverter.ParseHorizontalAlignment(format.HorizontalAlignment);
            if (result.IsFailure)
            {
                return result;
            }
            style.Alignment.Horizontal = result.Value;
        }

        if (format.VerticalAlignment is not null)
        {
            var result = SpreadsheetFormatConverter.ParseVerticalAlignment(format.VerticalAlignment);
            if (result.IsFailure)
            {
                return result;
            }
            style.Alignment.Vertical = result.Value;
        }

        if (format.WrapText.HasValue)
        {
            style.Alignment.WrapText = format.WrapText.Value;
        }

        if (format.NumberFormat is not null)
        {
            style.NumberFormat.Format = format.NumberFormat;
        }

        return Result.Ok();
    }

    private static Result ApplyTextFormat(IXLStyle style, SpreadsheetTextFormat textFormat, IXLWorkbook workbook)
    {
        if (textFormat.Bold.HasValue)
        {
            style.Font.Bold = textFormat.Bold.Value;
        }

        if (textFormat.Italic.HasValue)
        {
            style.Font.Italic = textFormat.Italic.Value;
        }

        if (textFormat.Underline.HasValue)
        {
            style.Font.Underline = textFormat.Underline.Value
                ? XLFontUnderlineValues.Single
                : XLFontUnderlineValues.None;
        }

        if (textFormat.Strikethrough.HasValue)
        {
            style.Font.Strikethrough = textFormat.Strikethrough.Value;
        }

        if (textFormat.FontFamily is not null)
        {
            if (textFormat.FontFamily.Length == 0)
            {
                // Empty string resets the font family to the workbook default.
                style.Font.FontName = workbook.Style.Font.FontName;
            }
            else
            {
                style.Font.FontName = textFormat.FontFamily;
            }
        }

        if (textFormat.FontSize.HasValue)
        {
            if (textFormat.FontSize.Value <= 0)
            {
                // Non-positive value resets the font size to the workbook default.
                style.Font.FontSize = workbook.Style.Font.FontSize;
            }
            else
            {
                style.Font.FontSize = textFormat.FontSize.Value;
            }
        }

        if (textFormat.ForegroundColor is not null)
        {
            if (textFormat.ForegroundColor.Length == 0)
            {
                // Empty string resets the font colour to the workbook default.
                style.Font.FontColor = workbook.Style.Font.FontColor;
            }
            else
            {
                var colorResult = SpreadsheetFormatConverter.ParseColor(textFormat.ForegroundColor);
                if (colorResult.IsFailure)
                {
                    return colorResult;
                }
                style.Font.FontColor = colorResult.Value;
            }
        }

        return Result.Ok();
    }

    private static Result ApplyBorders(IXLBorder border, SpreadsheetBordersSpec borders, IXLWorkbook workbook)
    {
        if (borders.Top is not null)
        {
            var result = ApplyBorderSide(border, borders.Top, BorderPosition.Top, workbook);
            if (result.IsFailure)
            {
                return result;
            }
        }

        if (borders.Bottom is not null)
        {
            var result = ApplyBorderSide(border, borders.Bottom, BorderPosition.Bottom, workbook);
            if (result.IsFailure)
            {
                return result;
            }
        }

        if (borders.Left is not null)
        {
            var result = ApplyBorderSide(border, borders.Left, BorderPosition.Left, workbook);
            if (result.IsFailure)
            {
                return result;
            }
        }

        if (borders.Right is not null)
        {
            var result = ApplyBorderSide(border, borders.Right, BorderPosition.Right, workbook);
            if (result.IsFailure)
            {
                return result;
            }
        }

        return Result.Ok();
    }

    private static Result ApplyBorderSide(IXLBorder border, SpreadsheetBorderSide side, BorderPosition position, IXLWorkbook workbook)
    {
        XLBorderStyleValues? borderStyle = null;
        if (side.Style is not null)
        {
            var styleResult = SpreadsheetFormatConverter.ParseBorderStyle(side.Style);
            if (styleResult.IsFailure)
            {
                return styleResult;
            }
            borderStyle = styleResult.Value;
        }

        XLColor? color = null;
        if (side.Color is not null)
        {
            if (side.Color.Length == 0)
            {
                // Empty string resets the border colour to the workbook default.
                color = ResolveDefaultBorderColor(workbook, position);
            }
            else
            {
                var colorResult = SpreadsheetFormatConverter.ParseColor(side.Color);
                if (colorResult.IsFailure)
                {
                    return colorResult;
                }
                color = colorResult.Value;
            }
        }

        switch (position)
        {
            case BorderPosition.Top:
                if (borderStyle.HasValue) border.TopBorder = borderStyle.Value;
                if (color is not null) border.TopBorderColor = color;
                break;
            case BorderPosition.Bottom:
                if (borderStyle.HasValue) border.BottomBorder = borderStyle.Value;
                if (color is not null) border.BottomBorderColor = color;
                break;
            case BorderPosition.Left:
                if (borderStyle.HasValue) border.LeftBorder = borderStyle.Value;
                if (color is not null) border.LeftBorderColor = color;
                break;
            case BorderPosition.Right:
                if (borderStyle.HasValue) border.RightBorder = borderStyle.Value;
                if (color is not null) border.RightBorderColor = color;
                break;
        }

        return Result.Ok();
    }

    private static XLColor ResolveDefaultBorderColor(IXLWorkbook workbook, BorderPosition position)
    {
        var defaultBorder = workbook.Style.Border;
        switch (position)
        {
            case BorderPosition.Top:
                return defaultBorder.TopBorderColor;
            case BorderPosition.Bottom:
                return defaultBorder.BottomBorderColor;
            case BorderPosition.Left:
                return defaultBorder.LeftBorderColor;
            case BorderPosition.Right:
                return defaultBorder.RightBorderColor;
            default:
                return defaultBorder.TopBorderColor;
        }
    }

    private static double ResolveColumnWidth(double specifiedWidth, IXLWorkbook workbook)
    {
        if (specifiedWidth < 0)
        {
            return workbook.ColumnWidth;
        }

        return specifiedWidth;
    }

    private static double ResolveRowHeight(double specifiedHeight, IXLWorkbook workbook)
    {
        if (specifiedHeight < 0)
        {
            return workbook.RowHeight;
        }

        return specifiedHeight;
    }

    private static int CountStyleProperties(SpreadsheetFormatSpec format)
    {
        int count = 0;
        if (format.TextFormat is not null) count++;
        if (format.BackgroundColor is not null) count++;
        if (format.Borders is not null) count++;
        if (format.HorizontalAlignment is not null) count++;
        if (format.VerticalAlignment is not null) count++;
        if (format.WrapText.HasValue) count++;
        if (format.NumberFormat is not null) count++;

        return count;
    }

    private static bool IsColumnRange(string range)
    {
        return range.Split(':').All(part => !string.IsNullOrEmpty(part) && part.All(char.IsLetter));
    }

    private static bool IsRowRange(string range)
    {
        return range.Split(':').All(part => !string.IsNullOrEmpty(part) && part.All(char.IsDigit));
    }
}
