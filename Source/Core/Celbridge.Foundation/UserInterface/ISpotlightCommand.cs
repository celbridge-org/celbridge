using Celbridge.Commands;

namespace Celbridge.UserInterface;

/// <summary>
/// Display a teaching-tip spotlight on a named UI landmark, or clear it when the
/// target is empty.
/// </summary>
public interface ISpotlightCommand : IExecutableCommand
{
    /// <summary>
    /// Identifier of the landmark to highlight. An empty string clears the
    /// current spotlight.
    /// </summary>
    string Target { get; set; }

    /// <summary>
    /// Callout text shown on the teaching tip. An empty string shows the tip with
    /// no text.
    /// </summary>
    string Label { get; set; }

    /// <summary>
    /// Auto-clear delay in milliseconds. Zero leaves the tip until it is cleared,
    /// replaced, or the user interacts with the target.
    /// </summary>
    int DurationMs { get; set; }
}
