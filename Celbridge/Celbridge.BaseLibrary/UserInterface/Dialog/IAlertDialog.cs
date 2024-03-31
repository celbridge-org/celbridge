namespace Celbridge.BaseLibrary.UserInterface.Dialog;

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
