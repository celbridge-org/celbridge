namespace Celbridge.WebHost;

/// <summary>
/// Stores favicon files by the address each one came from, so a page naming a known address costs no request.
/// </summary>
public interface IWebViewFaviconStore
{
    /// <summary>
    /// Returns the file stored for an icon address, or null when the store has none.
    /// </summary>
    Task<byte[]?> TryReadIconAsync(string iconAddress);

    /// <summary>
    /// Stores the file fetched from an icon address, replacing any earlier file.
    /// </summary>
    Task WriteIconAsync(string iconAddress, byte[] favicon);
}
