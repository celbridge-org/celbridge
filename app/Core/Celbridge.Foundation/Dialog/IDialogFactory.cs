using Celbridge.Validators;

namespace Celbridge.Dialog;

/// <summary>
/// Provides factory methods for creating various types of modal dialogs.
/// </summary>
public interface IDialogFactory
{
    /// <summary>
    /// Create an Alert Dialog with configurable title and message text.
    /// </summary>
    IAlertDialog CreateAlertDialog(string titleText, string messageText);

    /// <summary>
    /// Create a Confirmation Dialog with configurable title, message text, and optional button text.
    /// </summary>
    IConfirmationDialog CreateConfirmationDialog(string titleText, string messageText, string? primaryButtonText = null, string? secondaryButtonText = null);

    /// <summary>
    /// Create a Progress Dialog.
    /// </summary>
    IProgressDialog CreateProgressDialog();

    /// <summary>
    /// Create a New Project Dialog with template selection.
    /// </summary>
    INewProjectDialog CreateNewProjectDialog();

    /// <summary>
    /// Create an Input Text Dialog.
    /// </summary>
    IInputTextDialog CreateInputTextDialog(string titleText, string messageText, string defaultText, Range selectionRange, IValidator validator, string? submitButtonKey = null);

    /// <summary>
    /// Create an Add File Dialog.
    /// </summary>
    IAddFileDialog CreateAddFileDialog(string defaultFileName, Range selectionRange, IValidator validator);
}

