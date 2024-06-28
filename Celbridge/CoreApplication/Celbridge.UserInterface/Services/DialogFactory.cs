using Celbridge.BaseLibrary.Dialog;
using Celbridge.UserInterface.Views;

namespace Celbridge.UserInterface.Services;

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

    public IInputTextDialog CreateInputTextDialog(string titleText, string messageText)
    {
        var dialog = new InputTextDialog
        {
            TitleText = titleText,
            HeaderText = messageText,
        };
        return dialog;
    }
}
