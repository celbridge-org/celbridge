using Celbridge.BaseLibrary.UserInterface.Dialog;

namespace Celbridge.Services.UserInterface.Dialog;

public class DialogService : IDialogService
{
    private readonly IDialogFactory _dialogFactory;

    public DialogService(
        IDialogFactory dialogFactory)
    {
        _dialogFactory = dialogFactory;
    }

    public async Task ShowAlertDialogAsync(string titleText, string messageText, string closeText)
    {
        var dialog = _dialogFactory.CreateAlertDialog(titleText, messageText, closeText);

        await dialog.ShowDialogAsync();
    }

    private IProgressDialog? _progressDialog;

    public void ShowProgressDialog(string titleText)
    {
        Guard.IsNull(_progressDialog);

        _progressDialog = _dialogFactory.CreateProgressDialog(titleText);     
        _progressDialog.ShowDialog();
    }

    public void HideProgressDialog()
    {
        Guard.IsNotNull(_progressDialog);

        _progressDialog.HideDialog();
        _progressDialog = null;
    }
}
