using System.Runtime.CompilerServices;
using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.UserInterface.Platform;
using Celbridge.Validators;
using Celbridge.WebHost;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services.Dialogs;

public class DialogService : IDialogService
{
    private readonly ILogger<DialogService> _logger;
    private readonly IDialogFactory _dialogFactory;
    private readonly IFocusService _focusService;
    private readonly IManagedFocus _managedFocus;
    private readonly IWebViewFocusRegistry _webViewFocusRegistry;
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

    // Set once the open dialog has started to close.
    private bool _isDialogClosing;

    // Completes once the open dialog has closed and the keyboard has been returned.
    private Task _dialogClosed = Task.CompletedTask;

    // The requests waiting for the open dialog to close, each to open a dialog of its own.
    private int _requestsWaiting;

    // How long a request waits for a closing dialog. Closing takes about 200 ms, so a dialog still open after
    // this has stopped closing, and the request is refused.
    internal TimeSpan ClosingWaitLimit { get; set; } = TimeSpan.FromSeconds(5);

    public DialogService(
        ILogger<DialogService> logger,
        IDialogFactory dialogFactory,
        IFocusService focusService,
        IManagedFocus managedFocus,
        IWebViewFocusRegistry webViewFocusRegistry,
        IWorkspaceWrapper workspaceWrapper,
        IMessengerService messengerService)
    {
        _logger = logger;
        _dialogFactory = dialogFactory;
        _focusService = focusService;
        _managedFocus = managedFocus;
        _webViewFocusRegistry = webViewFocusRegistry;
        _workspaceWrapper = workspaceWrapper;
        _messengerService = messengerService;
        _answerScheduler = new DialogAnswerScheduler(logger, messengerService);

        _messengerService.Register<WorkspaceUnloadedMessage>(this, OnWorkspaceUnloaded);
    }

    public bool IsDialogOpen => _isDialogOpen;

    public async Task ShowAlertDialogAsync(string titleText, string messageText)
    {
        if (!await WaitForClosingDialogAsync())
        {
            RefuseSecondDialog();
            return;
        }

        var dialog = _dialogFactory.CreateAlertDialog(titleText, messageText);
        _answerScheduler.OnDialogShown(DialogKind.Alert);
        await ShowDialogAsync(dialog, async () =>
        {
            await dialog.ShowDialogAsync();
            return true;
        });
    }

    public async Task<Result<bool>> ShowConfirmationDialogAsync(string titleText, string messageText, ConfirmationDialogOptions? options = null)
    {
        if (!await WaitForClosingDialogAsync())
        {
            return RefuseSecondDialog();
        }

        var dialog = _dialogFactory.CreateConfirmationDialog(titleText, messageText, options);
        _answerScheduler.OnDialogShown(DialogKind.Confirmation);
        var showResult = await ShowDialogAsync(dialog, dialog.ShowDialogAsync);
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
        if (!await WaitForClosingDialogAsync())
        {
            RefuseSecondDialog();
            return;
        }

        var dialog = _dialogFactory.CreateSettingsDialog(sectionKey);

        try
        {
            await ShowDialogAsync(dialog, async () =>
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

    // Logs and fails a request to show a dialog while another one is open. The command queue and the macOS
    // menu bar are both held while a dialog is open, and a request made while one is closing waits for it,
    // so this should be unreachable. It is the backstop that turns whatever slips through into a
    // diagnosable failure rather than a ContentDialog throw.
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

    private async Task<T> ShowDialogAsync<T>(object dialog, Func<Task<T>> showDialog, [CallerMemberName] string dialogName = "")
    {
        _isDialogOpen = true;

        var dialogClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _dialogClosed = dialogClosed.Task;

        var contentDialog = dialog as ContentDialog;
        if (contentDialog is not null)
        {
            contentDialog.Closing += OnDialogClosing;
        }

        SetProgressDialogSuppressed(true);
        using var occlusionMonitorScope = MacOSModalOcclusionMonitor.BeginDialogScope(dialogName);

        // Where the keyboard goes back to, noted before the dialog takes it. By the time the dialog has
        // closed, the focus model has followed wherever the closing dialog left managed focus, which on the
        // Skia heads can be another panel.
        var focusedPanel = _focusService.FocusedPanel;
        var notedFocus = _managedFocus.NoteFocus();

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
            _isDialogClosing = false;

            if (contentDialog is not null)
            {
                contentDialog.Closing -= OnDialogClosing;
            }

            _messengerService.Send(new ModalDialogClosedMessage());

            // A request waiting for this dialog opens its own next, so the progress dialog stays hidden rather
            // than showing in between. Two dialogs showing at once would throw.
            SetProgressDialogSuppressed(_requestsWaiting > 0);

            ReturnKeyboard(focusedPanel, notedFocus);

            // Last, so a dialog waiting for this one opens after the keyboard has been returned.
            dialogClosed.SetResult();
        }
    }

    private void OnDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        // A dialog that cancels its closing stays open.
        if (!args.Cancel)
        {
            OnDialogStartedClosing();
        }
    }

    // Separate from the event handler, so the unit tests can start a dialog closing.
    internal void OnDialogStartedClosing()
    {
        _isDialogClosing = true;
    }

    // Returns whether a new dialog may open, waiting first for an open dialog that has started to close.
    // When a dialog starts to close, WinUI gives the keyboard back to the control that opened it, about 200 ms
    // before the dialog has closed. A key pressed on that control in that time asks for a dialog while this
    // one is still open, so the request waits for it to close. A request while a dialog is open and not
    // closing is refused. A caller that gets true opens its dialog straight away.
    private async Task<bool> WaitForClosingDialogAsync()
    {
        if (!_isDialogOpen)
        {
            return true;
        }

        if (!_isDialogClosing)
        {
            return false;
        }

        _requestsWaiting++;
        try
        {
            await Task.WhenAny(_dialogClosed, Task.Delay(ClosingWaitLimit));
        }
        finally
        {
            _requestsWaiting--;
        }

        // The dialog may have stopped closing, or another request waiting for it may have opened its own first.
        return !_isDialogOpen;
    }

    // A modal dialog moves keyboard focus into itself. Closing it usually hands focus back to the control that
    // opened it on the packaged Windows head, but not reliably on the Skia heads, which can leave it on the
    // first focusable element of another panel, or of the same one. Even the packaged Windows head can leave
    // it in the Explorer after a confirmation that opened as the New Project dialog closed. So the control is
    // given the keyboard back unless it already has it, and its panel takes over when the control no longer
    // can. That includes a control in no panel, such as a title bar button. A web surface keeps its focus
    // report through the dialog and only gets its caret back when its document takes focus again, so its
    // panel is always refocused.
    private void ReturnKeyboard(FocusPanelId focusedPanel, INotedFocus notedFocus)
    {
        if (!_webViewFocusRegistry.HasFocusedSurface)
        {
            if (notedFocus.IsFocusBack)
            {
                return;
            }

            if (notedFocus.TryReturnFocus())
            {
                _logger.LogTrace("Returned the keyboard to the control that held it before a dialog opened");
                return;
            }
        }

        // No panel held the keyboard, so there is none to give it back to.
        if (focusedPanel == FocusPanelId.None)
        {
            return;
        }

        _logger.LogTrace("Returning the keyboard to {Panel} after a dialog closed", focusedPanel);

        _focusService.RefocusPanel(focusedPanel);
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
        if (!await WaitForClosingDialogAsync())
        {
            return RefuseSecondDialog();
        }

        var dialog = _dialogFactory.CreateNewProjectDialog();
        return await ShowDialogAsync(dialog, dialog.ShowDialogAsync);
    }

    public async Task<Result<string>> ShowInputTextDialogAsync(string titleText, string messageText, string defaultText, Range selectionRange, IValidator validator, string? submitButtonKey = null)
    {
        if (!await WaitForClosingDialogAsync())
        {
            return RefuseSecondDialog();
        }

        var dialog = _dialogFactory.CreateInputTextDialog(titleText, messageText, defaultText, selectionRange, validator, submitButtonKey);
        _answerScheduler.OnDialogShown(DialogKind.InputText);
        return await ShowDialogAsync(dialog, dialog.ShowDialogAsync);
    }

    public async Task<Result<string>> ShowSecretInputDialogAsync(string titleText, string headerText, string? submitButtonKey = null)
    {
        if (!await WaitForClosingDialogAsync())
        {
            return RefuseSecondDialog();
        }

        var dialog = _dialogFactory.CreateSecretInputDialog(titleText, headerText, submitButtonKey);
        _answerScheduler.OnDialogShown(DialogKind.SecretInput);
        return await ShowDialogAsync(dialog, dialog.ShowDialogAsync);
    }

    public async Task<Result<NewFileConfig>> ShowNewFileDialogAsync(string defaultFileName, Range selectionRange, IValidator validator)
    {
        if (!await WaitForClosingDialogAsync())
        {
            return RefuseSecondDialog();
        }

        var dialog = _dialogFactory.CreateNewFileDialog(defaultFileName, selectionRange, validator);
        return await ShowDialogAsync(dialog, dialog.ShowDialogAsync);
    }

    public async Task<Result<ResourceKey>> ShowResourcePickerDialogAsync(IReadOnlyList<string> extensions, string? title = null, bool showPreview = false)
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return Result<ResourceKey>.Fail("Cannot show resource picker: no project is currently loaded.");
        }

        if (!await WaitForClosingDialogAsync())
        {
            return RefuseSecondDialog();
        }

        var dialog = _dialogFactory.CreateResourcePickerDialog(extensions, title, showPreview);
        _answerScheduler.OnDialogShown(DialogKind.ResourcePicker);
        return await ShowDialogAsync(dialog, dialog.ShowDialogAsync);
    }

    public async Task<Result<ResourceKey>> ShowFolderPickerDialogAsync(string? title = null)
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return Result<ResourceKey>.Fail("Cannot show folder picker: no project is currently loaded.");
        }

        if (!await WaitForClosingDialogAsync())
        {
            return RefuseSecondDialog();
        }

        var dialog = _dialogFactory.CreateFolderPickerDialog(title);
        _answerScheduler.OnDialogShown(DialogKind.ResourcePicker);
        return await ShowDialogAsync(dialog, dialog.ShowDialogAsync);
    }

    public async Task<Result<string>> ShowIconPickerDialogAsync(string searchText = "")
    {
        if (!await WaitForClosingDialogAsync())
        {
            return RefuseSecondDialog();
        }

        var dialog = _dialogFactory.CreateIconPickerDialog(searchText);
        _answerScheduler.OnDialogShown(DialogKind.IconPicker);
        return await ShowDialogAsync(dialog, dialog.ShowDialogAsync);
    }

    public async Task<Result<ChoiceDialogResult>> ShowChoiceDialogAsync(string titleText, string messageText, IReadOnlyList<string> options, int defaultIndex = 0, ChoiceDialogCheckbox? checkbox = null, string? primaryButtonText = null, string? secondaryButtonText = null)
    {
        if (!await WaitForClosingDialogAsync())
        {
            return RefuseSecondDialog();
        }

        var dialog = _dialogFactory.CreateChoiceDialog(titleText, messageText, options, defaultIndex, checkbox, primaryButtonText, secondaryButtonText);
        return await ShowDialogAsync(dialog, dialog.ShowDialogAsync);
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
