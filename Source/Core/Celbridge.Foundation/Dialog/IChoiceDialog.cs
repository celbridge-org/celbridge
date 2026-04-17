namespace Celbridge.Dialog;

/// <summary>
/// Configuration for the optional checkbox in a choice dialog.
/// </summary>
public record ChoiceDialogCheckbox(string Text, bool DefaultChecked = false);

/// <summary>
/// Result of a choice dialog interaction.
/// </summary>
public record ChoiceDialogResult(int SelectedIndex, bool CheckboxChecked);

/// <summary>
/// A modal dialog that lets the user pick from a list of named options.
/// Optionally includes a checkbox (e.g., "Use as default for this file type").
/// </summary>
public interface IChoiceDialog
{
    /// <summary>
    /// Present the choice dialog to the user.
    /// Returns the selected index and checkbox state, or fails if the user cancels.
    /// </summary>
    Task<Result<ChoiceDialogResult>> ShowDialogAsync();
}
