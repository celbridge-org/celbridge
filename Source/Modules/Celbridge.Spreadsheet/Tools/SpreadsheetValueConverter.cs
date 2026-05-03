using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Tools;

/// <summary>
/// Converts between ClosedXML cell values and JSON-friendly objects (numbers,
/// ISO 8601 date strings, booleans, strings, error strings, or null), and
/// renders cells as RFC 4180 CSV fields.
/// </summary>
internal static class SpreadsheetValueConverter
{
    /// <summary>
    /// Writes a JSON-typed value into a cell. Null clears the cell. Booleans,
    /// numbers (double, int, long, decimal), and strings round-trip directly.
    /// Anything else falls back to its string form. Strings are written as
    /// text even when they begin with '='; formula writes go through
    /// SetCellFormula instead.
    /// </summary>
    public static void SetCellValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                cell.Clear(XLClearOptions.Contents);
                break;
            case bool boolean:
                cell.Value = boolean;
                break;
            case double doubleValue:
                cell.Value = doubleValue;
                break;
            case int intValue:
                cell.Value = intValue;
                break;
            case long longValue:
                cell.Value = longValue;
                break;
            case decimal decimalValue:
                cell.Value = (double)decimalValue;
                break;
            case string text:
                cell.Value = text;
                break;
            default:
                cell.Value = value.ToString() ?? string.Empty;
                break;
        }
    }

    /// <summary>
    /// Writes a formula into a cell. The leading '=' on the formula text is
    /// stripped if present so the caller can pass either form.
    /// </summary>
    public static void SetCellFormula(IXLCell cell, string formula)
    {
        var stripped = formula;
        if (stripped.StartsWith('='))
        {
            stripped = stripped.Substring(1);
        }
        cell.FormulaA1 = stripped;
    }


    /// <summary>
    /// Maps an XLCellValue to a JSON-friendly object: number, ISO 8601 string
    /// for dates and time spans, bool, string, error code with leading '#', or
    /// null for blanks.
    /// </summary>
    public static object? ToJsonValue(XLCellValue cellValue)
    {
        if (cellValue.IsBlank)
        {
            return null;
        }

        if (cellValue.IsBoolean)
        {
            return cellValue.GetBoolean();
        }

        if (cellValue.IsNumber)
        {
            return cellValue.GetNumber();
        }

        if (cellValue.IsDateTime)
        {
            var dateTime = cellValue.GetDateTime();
            return dateTime.ToString("o");
        }

        if (cellValue.IsTimeSpan)
        {
            var timeSpan = cellValue.GetTimeSpan();
            return timeSpan.ToString();
        }

        if (cellValue.IsError)
        {
            var error = cellValue.GetError();
            return ErrorToString(error);
        }

        if (cellValue.IsText)
        {
            return cellValue.GetText();
        }

        return null;
    }

    /// <summary>
    /// Renders a JSON value as an RFC 4180 CSV field. Null becomes the empty
    /// field. Strings are quoted only when they contain a comma, double quote,
    /// or line break, with embedded double quotes doubled.
    /// </summary>
    public static string ToCsvField(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is bool boolean)
        {
            return boolean ? "TRUE" : "FALSE";
        }

        if (value is double number)
        {
            return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var text = value.ToString() ?? string.Empty;

        var needsQuoting = text.IndexOfAny(s_csvQuoteTriggers) >= 0;
        if (!needsQuoting)
        {
            return text;
        }

        var escaped = text.Replace("\"", "\"\"");
        return "\"" + escaped + "\"";
    }

    private static readonly char[] s_csvQuoteTriggers = new[] { ',', '"', '\r', '\n' };

    private static string ErrorToString(XLError error)
    {
        return error switch
        {
            XLError.NullValue => "#NULL!",
            XLError.DivisionByZero => "#DIV/0!",
            XLError.IncompatibleValue => "#VALUE!",
            XLError.CellReference => "#REF!",
            XLError.NameNotRecognized => "#NAME?",
            XLError.NumberInvalid => "#NUM!",
            XLError.NoValueAvailable => "#N/A",
            _ => "#ERROR!"
        };
    }
}
