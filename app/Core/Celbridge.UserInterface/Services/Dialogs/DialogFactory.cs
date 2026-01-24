using Celbridge.Dialog;
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
        var dialog = new NewProjectDialog();
        return dialog;
    }

    public IInputTextDialog CreateInputTextDialog(string titleText, string messageText, string defaultText, Range selectionRange, IValidator validator, string? submitButtonKey = null)
    {
        var dialog = new InputTextDialog
        {
            TitleText = titleText,
            HeaderText = messageText,
        };

        if (submitButtonKey is not null)
        {
            dialog.SubmitButtonKey = submitButtonKey;
        }

        dialog.ViewModel.Validator = validator;
        dialog.SetDefaultText(defaultText, selectionRange);

        return dialog;
    }

    public IAddFileDialog CreateAddFileDialog(string defaultFileName, Range selectionRange, IValidator validator)
    {
        var dialog = new AddFileDialog();

        dialog.ViewModel.Validator = validator;
        dialog.SetDefaultFileName(defaultFileName, selectionRange);

        return dialog;
    }
}

