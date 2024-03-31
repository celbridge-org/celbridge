using Celbridge.BaseLibrary.UserInterface.Dialog;

namespace Celbridge.Views.Dialogs;

public class DialogFactory : IDialogFactory
{
    public IAlertDialog CreateAlertDialog(string titleText, string messageText, string closeText)
    {
        var dialog = new AlertDialog();
        dialog.TitleText = titleText;
        dialog.MessageText = messageText;
        dialog.CloseText = closeText;
        return dialog;
    }

    public IProgressDialog CreateProgressDialog(string titleText, string cancelText)
    {
        var dialog = new ProgressDialog();
        dialog.TitleText = titleText;
        dialog.CancelText = cancelText;
        return dialog;
    }
}
