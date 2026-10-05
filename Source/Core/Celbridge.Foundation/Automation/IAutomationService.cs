using Celbridge.WebHost;

namespace Celbridge.Automation;

/// <summary>
/// A control's frame in device-independent pixels, measured from the top left of the main window's content. A
/// control above the content, such as a title bar button, has a negative Y.
/// </summary>
public record ControlBounds(double X, double Y, double Width, double Height);

/// <summary>
/// One of the application's own controls, whether the application or the platform draws it. IsChecked is null for
/// a control that cannot be toggled or selected, and Value is null for a control that has no value.
/// HasKeyboardFocus is true for the one control that holds the keyboard.
/// </summary>
public record ControlInfo(
    string AutomationId,
    string Name,
    string ControlType,
    string ClassName,
    ControlBounds Bounds,
    bool IsEnabled,
    bool? IsChecked,
    string? Value,
    bool HasKeyboardFocus = false);

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
/// One element of a document's page. Selector is unique within the element's frame, and Bounds uses the same
/// coordinates as ControlBounds. IsInView is true when the element's center lies inside every enclosing frame and
/// inside the web view. Value is the text of a text field, a text area or a select element, and is null for any
/// other element. IsChecked is null for an element with no on or off state. IsFocused is true when the element
/// has keyboard focus.
/// </summary>
public record PageElementInfo(
    string Tag,
    string Selector,
    string Role,
    string AccessibleName,
    bool IsVisible,
    ControlBounds Bounds,
    bool IsInView,
    string Text,
    string? Value,
    bool? IsChecked,
    bool IsDisabled,
    bool IsFocused);

/// <summary>
/// The elements a lookup found in one frame of a document's page. It also carries the frame's name, the match
/// count before the results were capped, the bounds of the web view that shows the page, the page's device pixel
/// ratio, and the size and rasterization scale of the main window's content.
/// </summary>
public record PageElementSnapshot(
    string Frame,
    int TotalMatches,
    IReadOnlyList<PageElementInfo> Elements,
    ControlBounds WebViewBounds,
    double DevicePixelRatio,
    double ContentWidth,
    double ContentHeight,
    double RasterizationScale);

/// <summary>
/// Finds and invokes the application's own controls, and finds the elements of a document's page, for test
/// automation. Every call returns while a modal dialog is open.
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

    /// <summary>
    /// Finds the elements that the query matches in one frame of a document's page, each with its bounds in the
    /// window. Fails when the document has no page the tools can reach, or when its web view is hidden.
    /// </summary>
    Task<Result<PageElementSnapshot>> FindPageElementsAsync(ResourceKey resource, QueryOptions options);
}
