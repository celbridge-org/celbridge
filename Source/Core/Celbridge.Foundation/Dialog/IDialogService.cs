using Celbridge.Projects;
using Celbridge.Validators;

namespace Celbridge.Dialog;

/// <summary>
/// Identifies the dialog kinds that support automated answers.
/// </summary>
public enum DialogKind
{
    Alert,
    Confirmation,
    InputText,
    SecretInput,
    ResourcePicker,
}

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
    /// Display a Confirmation Dialog with configurable title and message text.
    /// Pass options to override the button labels or mark the action as destructive.
    /// </summary>
    Task<Result<bool>> ShowConfirmationDialogAsync(string titleText, string messageText, ConfirmationDialogOptions? options = null);

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
    /// Display a Secret Input Dialog that masks the entered value, for secrets
    /// such as an API key. Returns the entered secret, or fails when cancelled.
    /// </summary>
    Task<Result<string>> ShowSecretInputDialogAsync(string titleText, string headerText, string? submitButtonKey = null);

    /// <summary>
    /// Display an Add File Dialog with file type selection.
    /// </summary>
    Task<Result<NewFileConfig>> ShowNewFileDialogAsync(string defaultFileName, Range selectionRange, IValidator validator);

    /// <summary>
    /// Display a Resource Picker Dialog filtered to the specified file extensions.
    /// Fails if no project is currently loaded.
    /// </summary>
    Task<Result<ResourceKey>> ShowResourcePickerDialogAsync(IReadOnlyList<string> extensions, string? title = null, bool showPreview = false);

    /// <summary>
    /// Display a Choice Dialog that lets the user pick from a list of named options.
    /// When checkbox is provided, shows a checkbox below the options.
    /// Optional primaryButtonText and secondaryButtonText override the default OK/Cancel labels.
    /// Returns the selected index and checkbox state, or fails if the user cancels.
    /// </summary>
    Task<Result<ChoiceDialogResult>> ShowChoiceDialogAsync(string titleText, string messageText, IReadOnlyList<string> options, int defaultIndex = 0, ChoiceDialogCheckbox? checkbox = null, string? primaryButtonText = null, string? secondaryButtonText = null);

    /// <summary>
    /// Schedule an automated answer for the next modal dialog of the named
    /// kind. The delay timer begins when that dialog is displayed. If a dialog
    /// of a different kind appears first, the schedule stays pending. A
    /// subsequent call overwrites the schedule.
    /// </summary>
    void ScheduleAnswer(DialogKind dialogKind, string payload = "", int delayMs = 250);
}

