namespace Celbridge.UserInterface;

/// <summary>
/// Delivers synthetic input into the application's own event queue, for input an external test harness
/// cannot deliver. Runs on the UI thread rather than through the command queue, so a press still lands
/// while a modal dialog holds that queue — which is when a test most needs to send one. Supported on
/// macOS; every other platform reports the operation as unsupported.
/// </summary>
public interface IInputSimulationService
{
    /// <summary>
    /// Presses the named key, holding the named modifiers. The key name is one the simulator knows and the
    /// modifiers are a comma-separated list of "command", "control", "shift" and "option"; an unknown name
    /// on either fails with the list of names that are accepted.
    /// </summary>
    Task<Result> PressKeyAsync(string key, string modifiers);
}
