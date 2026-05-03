namespace Celbridge.Spreadsheet;

/// <summary>
/// Optional parameters for ISpreadsheetReader.ReadSheet. Range is A1 notation
/// without a sheet qualifier (e.g. "B2:D10"). Null reads the sheet's used range.
/// Headers true treats the first row in the range as column names and emits
/// row objects keyed by header. Offset and Limit page large sheets. Limit zero
/// applies the reader's default page size.
/// </summary>
public record SpreadsheetReadOptions(
    string? Range = null,
    SpreadsheetReadMode Mode = SpreadsheetReadMode.Values,
    bool Headers = false,
    int Offset = 0,
    int Limit = 0);
