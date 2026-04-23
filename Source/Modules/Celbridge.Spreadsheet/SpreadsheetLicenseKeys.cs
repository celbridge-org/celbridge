using System.Text;

namespace Celbridge.Spreadsheet;

internal static partial class SpreadsheetLicenseKeys
{
    internal static string LicenseKey { get; private set; } = string.Empty;
    internal static string DesignerLicenseKey { get; private set; } = string.Empty;

    private static string Decode(byte[] encoded, byte key)
    {
        var decoded = new byte[encoded.Length];
        for (var i = 0; i < encoded.Length; i++)
        {
            decoded[i] = (byte)(encoded[i] ^ key);
        }
        return Encoding.UTF8.GetString(decoded);
    }
}
