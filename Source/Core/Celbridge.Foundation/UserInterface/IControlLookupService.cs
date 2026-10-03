namespace Celbridge.UserInterface;

/// <summary>
/// What a control lookup matches. Each field that is not empty must equal the control's own value, and an empty
/// field matches every control.
/// </summary>
public record ControlQuery(string AutomationId, string Name, string ControlType);

/// <summary>
/// A control's frame in device-independent pixels, measured from the top left of the main window's content.
/// </summary>
public record ControlBounds(double X, double Y, double Width, double Height);

/// <summary>
/// One of the application's own controls, as its automation peer describes it. IsChecked is null for a control
/// that cannot be toggled or selected, and Value is null for a control that holds no value.
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
/// The controls a lookup found, with the size and rasterization scale of the main window's content.
/// </summary>
public record ControlLookupResult(
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
/// Finds and invokes the application's own controls the way assistive technology does, for test automation. It
/// searches the main window and its open popups, and answers while a modal dialog holds the command queue.
/// </summary>
public interface IControlLookupService
{
    /// <summary>
    /// Finds every showing control that matches the query, in tree order with the open popups' controls last.
    /// Fails when every field of the query is empty.
    /// </summary>
    Task<Result<ControlLookupResult>> FindControlsAsync(ControlQuery query);

    /// <summary>
    /// Performs the default action of the first showing, enabled control that matches the query and has one.
    /// Fails when no such control is found.
    /// </summary>
    Task<Result<ControlInvocation>> InvokeControlAsync(ControlQuery query);
}
