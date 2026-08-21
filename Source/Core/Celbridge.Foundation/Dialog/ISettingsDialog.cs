namespace Celbridge.Dialog;

/// <summary>
/// A modal dialog for viewing and changing the application settings.
/// </summary>
public interface ISettingsDialog
{
    /// <summary>
    /// Present the settings dialog to the user.
    /// The async call completes when the user closes the dialog.
    /// </summary>
    Task ShowDialogAsync();
}
