namespace Celbridge.Dialog;

/// <summary>
/// A modal dialog for entering a secret value such as an API key. The input is
/// masked and the value is never shown in plain text.
/// </summary>
public interface ISecretInputDialog
{
    /// <summary>
    /// The localization key for the submit button text.
    /// Defaults to "DialogButton_Ok" if not set.
    /// </summary>
    string SubmitButtonKey { get; set; }

    /// <summary>
    /// Present the dialog to the user. The async call completes when the dialog
    /// closes. Returns the entered secret, or a failure when the user cancels.
    /// </summary>
    Task<Result<string>> ShowDialogAsync();
}
