namespace Celbridge.Automation;

/// <summary>
/// A control's frame in device-independent pixels, measured from the top left of the main window's content. A
/// control above the content, such as a button in the window's title bar, has a negative Y.
/// </summary>
public record ControlBounds(double X, double Y, double Width, double Height);

/// <summary>
/// One of the application's own controls, as its automation peer describes it. A control the platform draws
/// natively is described as the platform's accessibility describes it. IsChecked is null for a control that cannot
/// be toggled or selected, and Value is null for a control that holds no value.
/// </summary>
public record ControlInfo(
    string AutomationId,
    string Name,
    string ControlType,
    string ClassName,
    ControlBounds Bounds,
    bool IsEnabled,
    bool? IsChecked,
    string? Value);

/// <summary>
/// The application's showing controls, with the size and rasterization scale of the main window's content.
/// </summary>
public record ControlSnapshot(
    IReadOnlyList<ControlInfo> Controls,
    double ContentWidth,
    double ContentHeight,
    double RasterizationScale);

/// <summary>
/// The default action a control performs when it is invoked.
/// </summary>
public enum ControlAction
{
    Invoke,
    Toggle,
    Expand,
    Select
}

/// <summary>
/// The control an invocation acted on, and the action the control performed.
/// </summary>
public record ControlInvocation(ControlInfo Control, ControlAction Action);

/// <summary>
/// Reads and acts on the application's own controls the way assistive technology does, for test automation. It
/// covers the main window, its open popups and the parts the platform draws natively, such as a menu bar. It
/// answers while a modal dialog holds the command queue.
/// </summary>
public interface IAutomationService
{
    /// <summary>
    /// Returns every showing control. The window's controls come first in tree order, then the open popups'
    /// controls, then the native controls.
    /// </summary>
    Task<Result<ControlSnapshot>> GetControlsAsync();

    /// <summary>
    /// Performs the default action of the first showing, enabled control that the match accepts and that has a
    /// default action. Fails when there is no such control.
    /// </summary>
    Task<Result<ControlInvocation>> InvokeControlAsync(Func<ControlInfo, bool> match);
}
