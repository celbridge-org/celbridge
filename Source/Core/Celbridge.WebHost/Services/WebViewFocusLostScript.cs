namespace Celbridge.WebHost;

/// <summary>
/// The script that reports the keyboard leaving a page. The managed layer cannot see this: on the packaged Windows
/// head the web content lives in its own child window, so a click on the caption or on any non-focusable region
/// moves the keyboard off it without moving managed focus at all. Every view installs it at document start.
/// </summary>
internal static class WebViewFocusLostScript
{
    // The source file is Web/celbridge-client/core/focus-lost-reporter.js, embedded under this name.
    private const string ResourceName = "Celbridge.WebHost.FocusLostReporter.js";

    /// <summary>
    /// The script's source, read once from the assembly.
    /// </summary>
    public static string Source { get; } = ReadSource();

    private static string ReadSource()
    {
        var readResult = EmbeddedResourceReader.ReadText(typeof(WebViewFocusLostScript).Assembly, ResourceName);
        if (readResult.IsFailure)
        {
            throw new InvalidOperationException(readResult.FirstErrorMessage);
        }

        return readResult.Value;
    }
}
