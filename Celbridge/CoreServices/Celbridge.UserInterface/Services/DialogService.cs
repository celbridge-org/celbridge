using Celbridge.Dialog;
using Celbridge.Foundation;
using Celbridge.Projects;
using Celbridge.Validators;

namespace Celbridge.UserInterface.Services;

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

    public async Task ShowAlertDialogAsync(string titleText, string messageText)
    {
        var dialog = _dialogFactory.CreateAlertDialog(titleText, messageText);

        SuppressProgressDialog(true);

        await dialog.ShowDialogAsync();

        SuppressProgressDialog(false);
    }

    public async Task<Result<bool>> ShowConfirmationDialogAsync(string titleText, string messageText)
    {
        var dialog = _dialogFactory.CreateConfirmationDialog(titleText, messageText);

        SuppressProgressDialog(true);

        var showResult = await dialog.ShowDialogAsync();

        SuppressProgressDialog(false);

        return Result<bool>.Ok(showResult);
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

    public async Task<Result<string>> ShowInputTextDialogAsync(string titleText, string messageText, string defaultText, Range selectionRange, IValidator validator)
    {
        var dialog = _dialogFactory.CreateInputTextDialog(titleText, messageText, defaultText, selectionRange, validator);

        SuppressProgressDialog(true);

        var showResult = await dialog.ShowDialogAsync();

        SuppressProgressDialog(false);

        return showResult;
    }
}
