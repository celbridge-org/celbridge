using Celbridge.Commands;

namespace Celbridge.UserInterface;

/// <summary>
/// Delivers a synthetic key press into the application's own event queue, for keys an external test
/// harness cannot send. Supported on macOS; every other platform reports the operation as unsupported.
/// </summary>
public interface ISimulateInputCommand : IExecutableCommand
{
    /// <summary>
    /// Name of the key to press, e.g. "Escape". Only the non-printable keys the simulator names are
    /// accepted; an unknown name fails with the list of names it does accept.
    /// </summary>
    string Key { get; set; }

    /// <summary>
    /// Modifier keys held for the press, as a comma-separated list of "command", "control", "shift" and
    /// "option". Empty for an unmodified press.
    /// </summary>
    string Modifiers { get; set; }
}
