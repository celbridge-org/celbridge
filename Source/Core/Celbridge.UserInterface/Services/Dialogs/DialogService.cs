using System.Runtime.CompilerServices;
using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.UserInterface.Platform;
using Celbridge.Validators;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services.Dialogs;

public class DialogService : IDialogService
{
    private readonly ILogger<DialogService> _logger;
    private readonly IDialogFactory _dialogFactory;
    private readonly IFocusService _focusService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IMessengerService _messengerService;
    private readonly DialogAnswerScheduler _answerScheduler;
    private readonly object _tokenLock = new();
    private IProgressDialog? _progressDialog;
    private IDisposable? _progressDialogOcclusionScope;
    private bool _suppressProgressDialog;
    private List<IProgressDialogToken> _progressDialogTokens = [];

    // Read by the command loop, which does not run on the UI thread that writes it.
    private volatile bool _isDialogOpen;

    public DialogService(
        ILogger<DialogService> logger,
        IDialogFactory dialogFactory,
        IFocusService focusService,
        IWorkspaceWrapper workspaceWrapper,
        IMessengerService messengerService)
    {
        _logger = logger;
        _dialogFactory = dialogFactory;
        _focusService = focusService;
        _workspaceWrapper = workspaceWrapper;
        _messengerService = messengerService;
        _answerScheduler = new DialogAnswerScheduler(logger, messengerService);

        _messengerService.Register<WorkspaceUnloadedMessage>(this, OnWorkspaceUnloaded);
    }

    public bool IsDialogOpen => _isDialogOpen;

    public async Task ShowAlertDialogAsync(string titleText, string messageText)
    {
        if (IsDialogOpen)
        {
            RefuseSecondDialog();
            return;
        }

        var dialog = _dialogFactory.CreateAlertDialog(titleText, messageText);
        _answerScheduler.OnDialogShown(DialogKind.Alert);
        await ShowDialogAsync(async () =>
        {
            await dialog.ShowDialogAsync();
            return true;
        });
    }

    public async Task<Result<bool>> ShowConfirmationDialogAsync(string titleText, string messageText, ConfirmationDialogOptions? options = null)
    {
        if (IsDialogOpen)
        {
            return RefuseSecondDialog();
        }

        var dialog = _dialogFactory.CreateConfirmationDialog(titleText, messageText, options);
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

    public async Task ShowSettingsDialogAsync(string sectionKey)
    {
        if (IsDialogOpen)
        {
            RefuseSecondDialog();
            return;
        }

        var dialog = _dialogFactory.CreateSettingsDialog(sectionKey);

        try
        {
            await ShowDialogAsync(async () =>
            {
                await dialog.ShowDialogAsync();
                return true;
            });
        }
        catch (Exception exception)
        {
            // Callers start this without awaiting it, so a failure here has nowhere else to surface.
            _logger.LogError(exception, "Failed to show the settings dialog");
        }
    }

    // Logs and fails a request to show a dialog while another one is on screen. The command queue and the
    // macOS menu bar are both held while a dialog is open, so this should be unreachable. It is the
    // backstop that turns whatever slips through into a diagnosable failure rather than a ContentDialog
    // throw.
    private Result.FailureResult RefuseSecondDialog([CallerMemberName] string dialogName = "")
    {
        _logger.LogError("Cannot show dialog '{DialogName}' because another dialog is already open", dialogName);

        return Result.Fail($"Cannot show dialog '{dialogName}' because another dialog is already open.");
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

    private async Task<T> ShowDialogAsync<T>(Func<Task<T>> showDialog, [CallerMemberName] string dialogName = "")
    {
        _isDialogOpen = true;
        SetProgressDialogSuppressed(true);
        using var occlusionMonitorScope = MacOSModalOcclusionMonitor.BeginDialogScope(dialogName);

        // A hosted web surface reports the dialog taking the keyboard as an ordinary blur, which would
        // otherwise clear the focused panel and leave nothing for the refocus below to return to.
        _messengerService.Send(new ModalDialogOpenedMessage());

        try
        {
            return await showDialog();
        }
        finally
        {
            // Cleared first so the command queue starts draining as the dialog comes down.
            _isDialogOpen = false;

            _messengerService.Send(new ModalDialogClosedMessage());

            SetProgressDialogSuppressed(false);

            // A modal dialog moves keyboard focus into itself; on the Skia heads closing it does not
            // reliably return focus to the panel it came from. Return keyboard focus to the focused panel
            // so the focus indicator's panel is the keyboard target again.
            _focusService.RefocusFocusedPanel();
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
                _progressDialogOcclusionScope = MacOSModalOcclusionMonitor.BeginDialogScope("ProgressDialog");
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
                _progressDialogOcclusionScope?.Dispose();
                _progressDialogOcclusionScope = null;
            }
        }
    }

    public async Task<Result<NewProjectConfig>> ShowNewProjectDialogAsync()
    {
        if (IsDialogOpen)
        {
            return RefuseSecondDialog();
        }

        var dialog = _dialogFactory.CreateNewProjectDialog();
        return await ShowDialogAsync(dialog.ShowDialogAsync);
    }

    public async Task<Result<string>> ShowInputTextDialogAsync(string titleText, string messageText, string defaultText, Range selectionRange, IValidator validator, string? submitButtonKey = null)
    {
        if (IsDialogOpen)
        {
            return RefuseSecondDialog();
        }

        var dialog = _dialogFactory.CreateInputTextDialog(titleText, messageText, defaultText, selectionRange, validator, submitButtonKey);
        _answerScheduler.OnDialogShown(DialogKind.InputText);
        return await ShowDialogAsync(dialog.ShowDialogAsync);
    }

    public async Task<Result<string>> ShowSecretInputDialogAsync(string titleText, string headerText, string? submitButtonKey = null)
    {
        if (IsDialogOpen)
        {
            return RefuseSecondDialog();
        }

        var dialog = _dialogFactory.CreateSecretInputDialog(titleText, headerText, submitButtonKey);
        _answerScheduler.OnDialogShown(DialogKind.SecretInput);
        return await ShowDialogAsync(dialog.ShowDialogAsync);
    }

    public async Task<Result<NewFileConfig>> ShowNewFileDialogAsync(string defaultFileName, Range selectionRange, IValidator validator)
    {
        if (IsDialogOpen)
        {
            return RefuseSecondDialog();
        }

        var dialog = _dialogFactory.CreateNewFileDialog(defaultFileName, selectionRange, validator);
        return await ShowDialogAsync(dialog.ShowDialogAsync);
    }

    public async Task<Result<ResourceKey>> ShowResourcePickerDialogAsync(IReadOnlyList<string> extensions, string? title = null, bool showPreview = false)
    {
        if (IsDialogOpen)
        {
            return RefuseSecondDialog();
        }

        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return Result<ResourceKey>.Fail("Cannot show resource picker: no project is currently loaded.");
        }

        var dialog = _dialogFactory.CreateResourcePickerDialog(extensions, title, showPreview);
        _answerScheduler.OnDialogShown(DialogKind.ResourcePicker);
        return await ShowDialogAsync(dialog.ShowDialogAsync);
    }

    public async Task<Result<string>> ShowIconPickerDialogAsync(string searchText = "")
    {
        if (IsDialogOpen)
        {
            return RefuseSecondDialog();
        }

        var dialog = _dialogFactory.CreateIconPickerDialog(searchText);
        _answerScheduler.OnDialogShown(DialogKind.IconPicker);
        return await ShowDialogAsync(dialog.ShowDialogAsync);
    }

    public async Task<Result<ChoiceDialogResult>> ShowChoiceDialogAsync(string titleText, string messageText, IReadOnlyList<string> options, int defaultIndex = 0, ChoiceDialogCheckbox? checkbox = null, string? primaryButtonText = null, string? secondaryButtonText = null)
    {
        if (IsDialogOpen)
        {
            return RefuseSecondDialog();
        }

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
