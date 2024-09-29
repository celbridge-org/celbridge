using Celbridge.Commands;

namespace Celbridge.Workspace;

/// <summary>
/// Display an alert dialogue.
/// </summary>
public interface IAlertCommand : IExecutableCommand
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
