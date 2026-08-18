using System.Globalization;

namespace Celbridge.Utilities;

/// <summary>
/// Composes the timestamp embedded in the name of a generated file, such as an application log or a
/// report. Fixed width and always in UTC, so ordering names lexically orders the files by time.
/// </summary>
public static class FileTimestamp
{
    /// <summary>
    /// ISO 8601 basic format with a UTC designator. The extended form cannot be used in a file name
    /// because ':' is not a legal path character on Windows.
    /// </summary>
    public const string Format = "yyyyMMdd'T'HHmmss'Z'";

    /// <summary>
    /// Formats an instant as a file name timestamp, converting it to UTC first.
    /// </summary>
    public static string Compose(DateTimeOffset instant)
    {
        return instant.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture);
    }
}
