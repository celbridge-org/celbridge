using Celbridge.BaseLibrary.UserInterface.Dialog;

namespace Celbridge.Views.Dialogs;

public class DialogFactory : IDialogFactory
{
    public IAlertDialog CreateAlertDialog(string titleText, string messageText, string closeText)
    {
        var dialog = new AlertDialog
        {
            TitleText = titleText,
            MessageText = messageText,
            CloseText = closeText
        };

        return dialog;
    }

    public IProgressDialog CreateProgressDialog()
    {
        var dialog = new ProgressDialog();
        return dialog;
    }

    public INewProjectDialog CreateNewProjectDialog()
    {
        var dialog = new NewProjectDialog();
        return dialog;
    }
}
