namespace Celbridge.WebHost;

/// <summary>
/// The script that reports the keyboard leaving a page. Every view installs it at document start. The managed layer
/// cannot see the keyboard leave. On the packaged Windows head the web content lives in its own child window. A
/// click on the caption or on any non-focusable region moves the keyboard off the page without moving managed focus.
/// </summary>
internal static class WebViewFocusLostScript
{
    // The embedded name of Web/celbridge-client/core/focus-lost-reporter.js. MSBuild changes the hyphens in folder
    // names to underscores, but not those in the file name.
    private const string ResourceName = "Celbridge.WebHost.Web.celbridge_client.core.focus-lost-reporter.js";

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
