using Celbridge.BaseLibrary.Validators;

namespace Celbridge.BaseLibrary.Dialog;

/// <summary>
/// A modal dialog that allows the user to input a text string.
/// </summary>
public interface IInputTextDialog
{
    /// <summary>
    /// Present the Input Text Dialog to the user.
    /// The async call completes when the user closes the dialog.
    /// Returns the text the user entered.
    /// </summary>
    Task<Result<string>> ShowDialogAsync();
}
