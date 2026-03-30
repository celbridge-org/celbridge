using Celbridge.Commands;

namespace Celbridge.UserInterface;

/// <summary>
/// Result of a user confirmation action.
/// </summary>
public record class ConfirmActionResult(bool Confirmed);

/// <summary>
/// Display a confirmation dialog and return whether the user confirmed.
/// </summary>
public interface IConfirmActionCommand : IExecutableCommand<ConfirmActionResult>
{
    /// <summary>
    /// Title text to display on the dialog.
    /// </summary>
    string Title { get; set; }

    /// <summary>
    /// Message text to display on the dialog.
    /// </summary>
    string Message { get; set; }
}
