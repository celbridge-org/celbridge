namespace Celbridge.BaseLibrary.UserInterface.Dialog;

public interface IDialogService
{
    /// <summary>
    /// Display an Alert Dialog with configurable title, message and close button text.
    /// </summary>
    Task ShowAlertDialogAsync(string titleText, string messageText, string closeText);

    /// <summary>
    /// Display a Progress Dialog with configurable title and cancel button text.
    /// Set the cancelText to an empty string to hide the cancel button.
    /// </summary>
    Task ShowProgressDialogAsync(string titleText, string cancelText);
}
