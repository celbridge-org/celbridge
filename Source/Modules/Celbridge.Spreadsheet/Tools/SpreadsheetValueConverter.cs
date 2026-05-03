using ClosedXML.Excel;

namespace Celbridge.Spreadsheet.Tools;

/// <summary>
/// Converts ClosedXML cell values into JSON-friendly objects (numbers, ISO 8601
/// date strings, booleans, strings, error strings, or null) and renders cells
/// as RFC 4180 CSV fields.
/// </summary>
internal static class SpreadsheetValueConverter
{
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
