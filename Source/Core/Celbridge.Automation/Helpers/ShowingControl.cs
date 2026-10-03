namespace Celbridge.Automation;

/// <summary>
/// A showing control's description, with a function that performs its default action. The function returns the
/// action it performed, or null when the control has none.
/// </summary>
internal record ShowingControl(ControlInfo Info, Func<ControlAction?> PerformDefaultAction);
