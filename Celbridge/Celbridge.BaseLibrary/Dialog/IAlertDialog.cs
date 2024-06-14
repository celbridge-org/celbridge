namespace Celbridge.BaseLibrary.Dialog;

/// <summary>
/// A modal dialog that displays an alert to the user.
/// </summary>
public interface IAlertDialog
{
    /// <summary>
    /// The title text displayed at the top of the alert dialog.
    /// </summary>
    string TitleText { get; set; }

    /// <summary>
    /// The message text displayed in the body of the alert dialog.
    /// </summary>
    string MessageText { get; set; }

    /// <summary>
    /// The text displayed on the close button of the alert dialog.
    /// </summary>
    string CloseText { get; set; }

    /// <summary>
    /// Present the alert dialog to the user.
    /// The async call completes when the user closes the dialog.
    /// </summary>
    Task ShowDialogAsync();
}
