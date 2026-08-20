namespace Celbridge.WebHost;

/// <summary>
/// Writes diagnostics reported by hosted web content into the application log. This is the whole of the
/// diagnostic contract with a page: one entry point that never affects behaviour, so reporting something new
/// from a page costs no protocol. Page text is untrusted and rate limited, because any package's content can
/// reach it.
/// </summary>
public interface IWebSurfaceLog
{
    /// <summary>
    /// Logs one entry reported by the named surface, at the level the page asked for. An unknown level is
    /// logged as debug. Messages beyond the surface's rate limit are dropped, with one warning per window.
    /// </summary>
    void Write(string surfaceName, string? level, string? message);
}
