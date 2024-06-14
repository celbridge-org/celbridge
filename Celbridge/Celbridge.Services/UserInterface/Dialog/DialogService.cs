using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface.Dialog;

namespace Celbridge.Services.UserInterface.Dialog;

public class DialogService : IDialogService
{
    private readonly IDialogFactory _dialogFactory;
    private IProgressDialog? _progressDialog;
    private bool _supressProgressDialog;
    private List<IProgressDialogToken> _progressDialogTokens = new();

    public DialogService(
        IDialogFactory dialogFactory)
    {
        _dialogFactory = dialogFactory;
    }

    public async Task ShowAlertDialogAsync(string titleText, string messageText, string closeText)
    {
        var dialog = _dialogFactory.CreateAlertDialog(titleText, messageText, closeText);

        SuppressProgressDialog(true);

        await dialog.ShowDialogAsync();

        SuppressProgressDialog(false);
    }

    public IProgressDialogToken AcquireProgressDialog(string titleText)
    {
        var token = new ProgressDialogToken(titleText);
        _progressDialogTokens.Add(token);
        UpdateProgressDialog();
        return token;
    }

    public void ReleaseProgressDialog(IProgressDialogToken token)
    {
        _progressDialogTokens.Remove(token);
        UpdateProgressDialog();
    }

    private void SuppressProgressDialog(bool suppressed)
    {
        _supressProgressDialog = suppressed;
        UpdateProgressDialog();
    }

    private void UpdateProgressDialog()
    {
        bool showDialog = _progressDialogTokens.Any() && !_supressProgressDialog;

        if (showDialog)
        {
            if (_progressDialog is null)
            {
                _progressDialog = _dialogFactory.CreateProgressDialog();
                _progressDialog.ShowDialog();
            }

            // Use the title text from the most recent token added
            _progressDialog.TitleText = _progressDialogTokens.Last().DialogTitle;
        }
        else
        {
            if (_progressDialog is not null)
            {
                _progressDialog.HideDialog();
                _progressDialog = null;
            }
        }
    }

    public async Task<Result<NewProjectConfig>> ShowNewProjectDialogAsync()
    {
        var dialog = _dialogFactory.CreateNewProjectDialog();

        SuppressProgressDialog(true);
        var showResult = await dialog.ShowDialogAsync();
        SuppressProgressDialog(false);
         
        return showResult; 
    }
}
