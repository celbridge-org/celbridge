using Celbridge.Dialog;
using Celbridge.Projects;
using Celbridge.Validators;

namespace Celbridge.UserInterface.Services.Dialogs;

public class DialogService : IDialogService
{
    private readonly IDialogFactory _dialogFactory;
    private readonly object _tokenLock = new();
    private IProgressDialog? _progressDialog;
    private bool _suppressProgressDialog;
    private List<IProgressDialogToken> _progressDialogTokens = [];

    public DialogService(
        IDialogFactory dialogFactory)
    {
        _dialogFactory = dialogFactory;
    }

    public async Task ShowAlertDialogAsync(string titleText, string messageText)
    {
        var dialog = _dialogFactory.CreateAlertDialog(titleText, messageText);
        await ShowDialogAsync(async () =>
        {
            await dialog.ShowDialogAsync();
            return true;
        });
    }

    public async Task<Result<bool>> ShowConfirmationDialogAsync(string titleText, string messageText)
    {
        var dialog = _dialogFactory.CreateConfirmationDialog(titleText, messageText);
        var showResult = await ShowDialogAsync(dialog.ShowDialogAsync);
        return Result<bool>.Ok(showResult);
    }

    public IProgressDialogToken AcquireProgressDialog(string titleText)
    {
        var token = new ProgressDialogToken(titleText, ReleaseProgressDialog);

        lock (_tokenLock)
        {
            _progressDialogTokens.Add(token);
        }

        UpdateProgressDialog();
        return token;
    }

    private void ReleaseProgressDialog(IProgressDialogToken token)
    {
        lock (_tokenLock)
        {
            _progressDialogTokens.Remove(token);
        }

        UpdateProgressDialog();
    }

    private void SetProgressDialogSuppressed(bool suppressed)
    {
        _suppressProgressDialog = suppressed;
        UpdateProgressDialog();
    }

    private async Task<T> ShowDialogAsync<T>(Func<Task<T>> showDialog)
    {
        SetProgressDialogSuppressed(true);
        try
        {
            return await showDialog();
        }
        finally
        {
            SetProgressDialogSuppressed(false);
        }
    }

    private void UpdateProgressDialog()
    {
        bool hasTokens;
        string? lastTokenTitle = null;

        lock (_tokenLock)
        {
            hasTokens = _progressDialogTokens.Count > 0;
            if (hasTokens)
            {
                lastTokenTitle = _progressDialogTokens[^1].DialogTitle;
            }
        }

        bool showDialog = hasTokens && !_suppressProgressDialog;

        if (showDialog)
        {
            if (_progressDialog is null)
            {
                _progressDialog = _dialogFactory.CreateProgressDialog();
                _progressDialog.ShowDialog();
            }

            // Use the title text from the most recent token added
            _progressDialog.TitleText = lastTokenTitle!;
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
        return await ShowDialogAsync(dialog.ShowDialogAsync);
    }

    public async Task<Result<string>> ShowInputTextDialogAsync(string titleText, string messageText, string defaultText, Range selectionRange, IValidator validator, string? submitButtonKey = null)
    {
        var dialog = _dialogFactory.CreateInputTextDialog(titleText, messageText, defaultText, selectionRange, validator, submitButtonKey);
        return await ShowDialogAsync(dialog.ShowDialogAsync);
    }

    public async Task<Result<AddFileConfig>> ShowAddFileDialogAsync(string defaultFileName, Range selectionRange, IValidator validator)
    {
        var dialog = _dialogFactory.CreateAddFileDialog(defaultFileName, selectionRange, validator);
        return await ShowDialogAsync(dialog.ShowDialogAsync);
    }
}
