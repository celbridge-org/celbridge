namespace Celbridge.Dialog;

/// <summary>
/// A modal dialog that shows the application's name and version, with links to its website and source.
/// </summary>
public interface IAboutDialog
{
    /// <summary>
    /// Present the about dialog to the user.
    /// The async call completes when the user closes the dialog.
    /// </summary>
    Task ShowDialogAsync();
}
