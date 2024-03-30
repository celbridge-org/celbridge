namespace Celbridge.BaseLibrary.UserInterface.Dialog;

public interface IDialogService
{
    /// <summary>
    /// Display an Alert Dialog with configurable title, message and close button text.
    /// </summary>
    Task ShowAlertAsync(string titleText, string messageText, string closeText);
}
