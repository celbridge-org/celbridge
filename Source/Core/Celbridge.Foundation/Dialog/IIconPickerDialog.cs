namespace Celbridge.Dialog;

/// <summary>
/// A modal dialog that allows the user to pick an icon from the supported icon set.
/// </summary>
public interface IIconPickerDialog
{
    /// <summary>
    /// Present the Icon Picker Dialog to the user.
    /// Returns the prefixed name of the chosen icon, or fails if the dialog was cancelled.
    /// </summary>
    Task<Result<string>> ShowDialogAsync();
}
