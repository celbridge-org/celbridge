namespace Celbridge.WebHost;

/// <summary>
/// A page's favicon: the address the icon came from, and the icon as encoded image bytes.
/// </summary>
public sealed record WebViewFavicon(string Address, byte[] Image);
