namespace Celbridge.WebHost;

/// <summary>
/// Applies the desired focus state derived from the focus model to the platform. The focused web surface
/// (when there is one) becomes the native focus target with managed focus parked; otherwise native focus
/// returns to the host window. Idempotent, so it is safe to call from any focus event.
/// </summary>
public interface IFocusReconciler
{
    /// <summary>
    /// Derives the desired focus state and applies it: managed focus first, then the native first
    /// responder.
    /// </summary>
    void Reconcile();
}
