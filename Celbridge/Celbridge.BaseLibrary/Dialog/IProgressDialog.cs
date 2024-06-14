namespace Celbridge.BaseLibrary.Dialog;

/// <summary>
/// A modal dialog that displays progress to the user.
/// </summary>
public interface IProgressDialog
{
    /// <summary>
    /// The title text displayed at the top of the progress dialog.
    /// </summary>
    string TitleText { get; set; }

    /// <summary>
    /// Present the progress dialog to the user.
    /// </summary>
    void ShowDialog();

    /// <summary>
    /// Hide the progress dialog.
    /// </summary>
    void HideDialog();
}
