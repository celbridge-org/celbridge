namespace Celbridge.Spreadsheet;

/// <summary>
/// Holds SpreadJS license keys.
/// The default values are empty strings. To provide real keys, create a
/// SpreadsheetLicenseKeys.private.cs file (gitignored) with a static constructor
/// that sets these fields.
/// </summary>
internal static partial class SpreadsheetLicenseKeys
{
    internal static string LicenseKey = "";
    internal static string DesignerLicenseKey = "";
}
