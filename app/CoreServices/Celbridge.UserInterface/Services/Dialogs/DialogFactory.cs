using Celbridge.Dialog;
using Celbridge.Projects;
using Celbridge.UserInterface.Views;
using Celbridge.Validators;

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
        var dialog = new NewProjectDialog(NewProjectConfigType.Standard);
        return dialog;
    }

    public INewProjectDialog CreateNewExampleProjectDialog()
    {
        // %%% NOTE - Going forwards I would imagine this being a separate dialog.
        //  I would personally like to remove all the path and name selection support from the Create New dialog and make it a control,
        //  and have it used by both the Create New Project dialog (which would have little else in it), and the Create New Project Dialog (which would
        //  have the lists of examples to select and so on, also).
        //  For now we will use a flag to change some rudimentary behaviour until this is ready to be overhauled.
        var dialog = new NewProjectDialog(NewProjectConfigType.Example);
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

    public INewFileDialog CreateNewFileDialog(string titleText, string headerText, string defaultFileName, Range selectionRange, IValidator validator)
    {
        var dialog = new NewFileDialog
        {
            TitleText = titleText,
            HeaderText = headerText,
        };

        dialog.ViewModel.Validator = validator;
        dialog.SetDefaultFileName(defaultFileName, selectionRange);

        return dialog;
    }
}
