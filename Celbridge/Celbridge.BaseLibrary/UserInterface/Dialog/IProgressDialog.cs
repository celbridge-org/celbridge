namespace Celbridge.BaseLibrary.UserInterface.Dialog;

public interface IProgressDialog
{
    /// <summary>
    /// Present the progress dialog to the user.
    /// The async call completes when the dialog closes.
    /// </summary>
    Task ShowDialogAsync();
}
