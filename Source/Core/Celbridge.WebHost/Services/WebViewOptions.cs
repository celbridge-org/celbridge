using Windows.UI;

namespace Celbridge.WebHost;

/// <summary>
/// The settings a web view is acquired with.
/// </summary>
public sealed record WebViewOptions
{
    /// <summary>
    /// The options a web view gets when its caller needs nothing else.
    /// </summary>
    public static readonly WebViewOptions Default = new();

    /// <summary>
    /// The color the view shows where the page draws nothing.
    /// </summary>
    public Color BackgroundColor { get; init; } = Colors.Transparent;

    /// <summary>
    /// Whether the page can be opened in the developer tools.
    /// </summary>
    public bool IsDevToolsEnabled { get; init; }

    /// <summary>
    /// Whether the user can zoom the page.
    /// </summary>
    public bool IsZoomEnabled { get; init; } = true;

    /// <summary>
    /// A token added to the page's User-Agent, such as "Celbridge/1.0.0". Null keeps the platform's User-Agent.
    /// </summary>
    public string? UserAgentToken { get; init; }
}
