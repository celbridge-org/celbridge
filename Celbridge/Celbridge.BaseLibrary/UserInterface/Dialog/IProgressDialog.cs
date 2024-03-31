namespace Celbridge.BaseLibrary.UserInterface.Dialog;

public interface IProgressDialog
{
    /// <summary>
    /// The title text displayed at the top of the progress dialog.
    /// </summary>
    string TitleText { get; set; }

    /// <summary>
    /// The text displayed on the cancel button.
    /// If this is empty, the cancel button is not displayed.
    /// </summary>
    string CancelText { get; set; }

    /// <summary>
    /// Present the progress dialog to the user.
    /// The async call completes when the dialog closes.
    /// </summary>
    Task ShowDialogAsync();
}
