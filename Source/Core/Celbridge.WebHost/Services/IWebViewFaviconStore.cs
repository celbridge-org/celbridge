namespace Celbridge.WebHost;

/// <summary>
/// Stores favicon images by the address of each icon.
/// </summary>
public interface IWebViewFaviconStore
{
    /// <summary>
    /// Returns the image stored for an icon address, or null when the store has none.
    /// </summary>
    Task<byte[]?> TryReadIconAsync(string iconAddress);

    /// <summary>
    /// Stores the image for an icon address, replacing any earlier image.
    /// </summary>
    Task WriteIconAsync(string iconAddress, byte[] favicon);
}
