using Celbridge.BaseLibrary.UserInterface.Dialog;

namespace Celbridge.Views.Dialogs;

public class DialogFactory : IDialogFactory
{
    public IAlertDialog CreateAlertDialog(string titleText, string messageText, string closeText)
    {
        var dialog = new AlertDialog();
        dialog.Title = titleText;
        dialog.MessageText = messageText;
        dialog.CloseText = closeText;
        return dialog;
    }
}
