using Celbridge.BaseLibrary.UserInterface.Dialog;

namespace Celbridge.Views.Dialogs;

public class DialogFactory : IDialogFactory
{
    public IAlertDialog CreateAlertDialog(string titleText, string messageText, string closeText)
    {
        var dialog = ServiceLocator.ServiceProvider.GetRequiredService<IAlertDialog>();

        dialog.TitleText = titleText;
        dialog.MessageText = messageText;
        dialog.CloseText = closeText;

        return dialog;
    }

    public IProgressDialog CreateProgressDialog(string titleText)
    {
        var dialog = ServiceLocator.ServiceProvider.GetRequiredService<IProgressDialog>();

        dialog.TitleText = titleText;
 
        return dialog;
    }
}
