using Celbridge.Projects;
using Celbridge.Validators;

namespace Celbridge.Dialog;

/// <summary>
/// Manages the display of modal dialogs to the user.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Display an Alert Dialog with configurable title and message text.
    /// </summary>
    Task ShowAlertDialogAsync(string titleText, string messageText);

    /// <summary>
    /// Display a Confirmation Dialog with configurable title, message text, and optional button text.
    /// </summary>
    Task<Result<bool>> ShowConfirmationDialogAsync(string titleText, string messageText, string? primaryButtonText = null, string? secondaryButtonText = null);

    /// <summary>
    /// Acquire a progress dialog token.
    /// The progress dialog will be displayed as long as any token is active, and will display the title of the
    /// most recently acquired token that is still active. The progress dialog is temporarily hidden while any other type 
    /// of dialog is displayed.
    /// Dispose the token to release it. The progress dialog is hidden when all tokens are released.
    /// </summary>
    IProgressDialogToken AcquireProgressDialog(string titleText);

    /// <summary>
    /// Display a New Project Dialog with template selection.
    /// </summary>
    Task<Result<NewProjectConfig>> ShowNewProjectDialogAsync();

    /// <summary>
    /// Display an Input Text Dialog.
    /// </summary>
    Task<Result<string>> ShowInputTextDialogAsync(string titleText, string messageText, string defaultText, Range selectionRange, IValidator validator, string? submitButtonKey = null);

    /// <summary>
    /// Display an Add File Dialog with file type selection.
    /// </summary>
    Task<Result<AddFileConfig>> ShowAddFileDialogAsync(string defaultFileName, Range selectionRange, IValidator validator);
}

