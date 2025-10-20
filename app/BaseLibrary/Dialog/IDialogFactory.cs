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
    /// Create an Confirmation Dialog with configurable title and message text.
    /// </summary>
    IConfirmationDialog CreateConfirmationDialog(string titleText, string messageText);


    /// <summary>
    /// Create a Progress Dialog.
    /// </summary>
    IProgressDialog CreateProgressDialog();

    /// <summary>
    /// Create a New Project Dialog.
    /// </summary>
    INewProjectDialog CreateNewProjectDialog();

    /// <summary>
    /// Create a New Example Project Dialog.
    /// </summary>
    INewProjectDialog CreateNewExampleProjectDialog();

    /// <summary>
    /// Create an Input Text Dialog.
    /// </summary>
    IInputTextDialog CreateInputTextDialog(string titleText, string messageText, string defaultText, Range selectionRange, IValidator validator);
}
