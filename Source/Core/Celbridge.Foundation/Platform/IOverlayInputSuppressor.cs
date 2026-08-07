namespace Celbridge.Platform;

/// <summary>
/// Keeps input meant for a managed overlay from also reaching hosted web views underneath it.
/// </summary>
public interface IOverlayInputSuppressor
{
    /// <summary>
    /// Stops hosted web views taking mouse input until the returned scope is disposed. Call when an overlay
    /// opens and dispose when it closes. Scopes nest, so an overlay opened over another keeps the
    /// suppression until both have closed. Does nothing on platforms where web views already take part in
    /// the normal input routing.
    /// </summary>
    IDisposable Suppress();
}
