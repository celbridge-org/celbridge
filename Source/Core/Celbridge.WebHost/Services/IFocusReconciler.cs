namespace Celbridge.WebHost;

/// <summary>
/// Applies the desired focus state derived from the focus model to the platform. The focused web surface
/// (when there is one) becomes the native focus target and the managed world yields the keyboard to it.
/// Otherwise native focus returns to the host window, and managed focus is given up as well when it rests
/// on an element that has left the visual tree. Idempotent, so it is safe to call from any focus event.
/// </summary>
public interface IFocusReconciler
{
    /// <summary>
    /// Derives the desired focus state and applies it: managed focus first, then the native first
    /// responder.
    /// </summary>
    void Reconcile();
}
