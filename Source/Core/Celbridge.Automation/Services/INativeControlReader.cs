namespace Celbridge.Automation;

/// <summary>
/// The native view that draws a managed element. IsShowing is false when the view is hidden or outside the main
/// window, and Bounds is then empty.
/// </summary>
internal record NativeView(bool IsShowing, ControlBounds Bounds);

/// <summary>
/// Reads the controls the platform draws natively, outside the visual tree, in the same coordinates as the
/// managed controls.
/// </summary>
internal interface INativeControlReader
{
    /// <summary>
    /// The visible native controls, such as the menu bar's items and the window's buttons.
    /// </summary>
    IReadOnlyList<ShowingControl> ReadControls();

    /// <summary>
    /// The native view that draws the element, or null when the element draws itself.
    /// </summary>
    NativeView? FindNativeView(FrameworkElement element);
}
