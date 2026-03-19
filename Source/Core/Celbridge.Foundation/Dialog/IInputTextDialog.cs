namespace Celbridge.Dialog;

/// <summary>
/// A modal dialog that allows the user to input a text string.
/// </summary>
public interface IInputTextDialog
{
    /// <summary>
    /// The localization key for the submit button text.
    /// Defaults to "DialogButton_Ok" if not set.
    /// </summary>
    string SubmitButtonKey { get; set; }

    /// <summary>
    /// Present the Input Text Dialog to the user.
    /// The async call completes when the user closes the dialog.
    /// Returns the text the user entered.
    /// </summary>
    Task<Result<string>> ShowDialogAsync();
}
