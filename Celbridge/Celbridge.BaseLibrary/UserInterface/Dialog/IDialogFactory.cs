namespace Celbridge.BaseLibrary.UserInterface.Dialog;

public interface IDialogFactory
{
    /// <summary>
    /// Create an Alert Dialog with configurable title, message and close button text.
    /// </summary>
    IAlertDialog CreateAlertDialog(string titleText, string messageText, string closeText);
}
