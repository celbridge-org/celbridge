namespace Celbridge.Dialog;

/// <summary>
/// A modal dialog that allows the user to pick a resource from the project.
/// </summary>
public interface IResourcePickerDialog
{
    /// <summary>
    /// Present the Resource Picker Dialog to the user.
    /// The async call completes when the user closes the dialog.
    /// Returns the selected resource key on success, or fails if the dialog was cancelled.
    /// </summary>
    Task<Result<ResourceKey>> ShowDialogAsync();
}
