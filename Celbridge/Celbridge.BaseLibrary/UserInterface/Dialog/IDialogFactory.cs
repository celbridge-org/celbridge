namespace Celbridge.BaseLibrary.UserInterface.Dialog;

public interface IDialogFactory
{
    /// <summary>
    /// Create an Alert Dialog with configurable title, message and close button text.
    /// </summary>
    IAlertDialog CreateAlertDialog(string titleText, string messageText, string closeText);


    /// <summary>
    /// Create an Progress Dialog with configurable title..
    /// </summary>
    IProgressDialog CreateProgressDialog(string titleText, string canceltext);
}
