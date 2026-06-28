using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Validators;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services.Dialogs;

public class DialogService : IDialogService
{
    private readonly IDialogFactory _dialogFactory;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IMessengerService _messengerService;
    private readonly DialogAnswerScheduler _answerScheduler;
    private readonly object _tokenLock = new();
    private IProgressDialog? _progressDialog;
    private bool _suppressProgressDialog;
    private List<IProgressDialogToken> _progressDialogTokens = [];

    public DialogService(
        ILogger<DialogService> logger,
        IDialogFactory dialogFactory,
        IWorkspaceWrapper workspaceWrapper,
        IMessengerService messengerService)
    {
        _dialogFactory = dialogFactory;
        _workspaceWrapper = workspaceWrapper;
        _messengerService = messengerService;
        _answerScheduler = new DialogAnswerScheduler(logger, messengerService);

        _messengerService.Register<WorkspaceUnloadedMessage>(this, OnWorkspaceUnloaded);
    }

    public async Task ShowAlertDialogAsync(string titleText, string messageText)
    {
        var dialog = _dialogFactory.CreateAlertDialog(titleText, messageText);
        _answerScheduler.OnDialogShown(DialogKind.Alert);
        await ShowDialogAsync(async () =>
        {
            await dialog.ShowDialogAsync();
            return true;
        });
    }

    public async Task<Result<bool>> ShowConfirmationDialogAsync(string titleText, string messageText, string? primaryButtonText = null, string? secondaryButtonText = null)
    {
        var dialog = _dialogFactory.CreateConfirmationDialog(titleText, messageText, primaryButtonText, secondaryButtonText);
        _answerScheduler.OnDialogShown(DialogKind.Confirmation);
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
        _answerScheduler.OnDialogShown(DialogKind.InputText);
        return await ShowDialogAsync(dialog.ShowDialogAsync);
    }

    public async Task<Result<string>> ShowSecretInputDialogAsync(string titleText, string headerText, string? submitButtonKey = null)
    {
        var dialog = _dialogFactory.CreateSecretInputDialog(titleText, headerText, submitButtonKey);
        _answerScheduler.OnDialogShown(DialogKind.SecretInput);
        return await ShowDialogAsync(dialog.ShowDialogAsync);
    }

    public async Task<Result<NewFileConfig>> ShowNewFileDialogAsync(string defaultFileName, Range selectionRange, IValidator validator)
    {
        var dialog = _dialogFactory.CreateNewFileDialog(defaultFileName, selectionRange, validator);
        return await ShowDialogAsync(dialog.ShowDialogAsync);
    }

    public async Task<Result<ResourceKey>> ShowResourcePickerDialogAsync(IReadOnlyList<string> extensions, string? title = null, bool showPreview = false)
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result<ResourceKey>.Fail("Cannot show resource picker: no project is currently loaded.");
        }

        var dialog = _dialogFactory.CreateResourcePickerDialog(extensions, title, showPreview);
        _answerScheduler.OnDialogShown(DialogKind.ResourcePicker);
        return await ShowDialogAsync(dialog.ShowDialogAsync);
    }

    public async Task<Result<ChoiceDialogResult>> ShowChoiceDialogAsync(string titleText, string messageText, IReadOnlyList<string> options, int defaultIndex = 0, ChoiceDialogCheckbox? checkbox = null, string? primaryButtonText = null, string? secondaryButtonText = null)
    {
        var dialog = _dialogFactory.CreateChoiceDialog(titleText, messageText, options, defaultIndex, checkbox, primaryButtonText, secondaryButtonText);
        return await ShowDialogAsync(dialog.ShowDialogAsync);
    }

    public void ScheduleAnswer(DialogKind dialogKind, string payload = "", int delayMs = 250)
    {
        _answerScheduler.Schedule(dialogKind, payload, delayMs);
    }

    private void OnWorkspaceUnloaded(object recipient, WorkspaceUnloadedMessage message)
    {
        _answerScheduler.Clear();
    }
}
