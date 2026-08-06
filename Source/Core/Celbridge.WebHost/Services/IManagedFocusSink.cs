namespace Celbridge.WebHost;

/// <summary>
/// An inert element that holds managed keyboard focus while a hosted web surface holds native focus. Needed
/// where giving a surface keyboard focus does not move managed focus, leaving the control the user last acted
/// on to consume the keys the platform still routes through the managed tree.
/// </summary>
public interface IManagedFocusSink
{
    /// <summary>
    /// Moves managed keyboard focus onto the sink. Returns false when the sink could not take focus, leaving
    /// focus with the control that currently holds it.
    /// </summary>
    bool TakeFocus();
}
