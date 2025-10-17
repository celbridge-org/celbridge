using Celbridge.Dialog;
using Celbridge.Validators;
using Celbridge.UserInterface.Views;

namespace Celbridge.UserInterface.Services.Dialogs;

public class DialogFactory : IDialogFactory
{
    public IAlertDialog CreateAlertDialog(string titleText, string messageText)
    {
        var dialog = new AlertDialog
        {
            TitleText = titleText,
            MessageText = messageText
        };

        return dialog;
    }

    public IConfirmationDialog CreateConfirmationDialog(string titleText, string messageText)
    {
        var dialog = new ConfirmationDialog
        {
            TitleText = titleText,
            MessageText = messageText
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
        var dialog = new NewProjectDialog(false);
        return dialog;
    }

    public INewProjectDialog CreateCreateExampleProjectDialog()
    {
        // %%% NOTE - Going forwards I would imagine this being a separate dialog.
        //  I would personally like to remove all the path and name selection support from the Create New dialog and make it a control,
        //  and have it used by both the Create New Project dialog (which would have little else in it), and the Create Example Project Dialog (which would
        //  have the lists of examples to select and so on, also).
        //  For now we will use a flag to change some rudimentary behaviour until this is ready to be overhauled.
        var dialog = new NewProjectDialog(true);
        return dialog;
    }

    public IInputTextDialog CreateInputTextDialog(string titleText, string messageText, string defaultText, Range selectionRange, IValidator validator)
    {
        var dialog = new InputTextDialog
        {
            TitleText = titleText,
            HeaderText = messageText,
        };

        dialog.ViewModel.Validator = validator;
        dialog.SetDefaultText(defaultText, selectionRange);

        return dialog;
    }
}
