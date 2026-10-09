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

    // How many requests are waiting for the open dialog to close so they can open their own.
    private int _requestsWaiting;

    // How long a request waits for a closing dialog before it's refused. Closing takes about 200 ms, so a dialog
    // that's still open after this long has stopped closing.
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

        _logger.LogInformation("Showing alert '{Title}': {Message}", titleText, messageText);

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

    public async Task ShowAboutDialogAsync()
    {
        if (!await WaitForClosingDialogAsync())
        {
            RefuseSecondDialog();
            return;
        }

        var dialog = _dialogFactory.CreateAboutDialog();
        await ShowDialogAsync(dialog, async () =>
        {
            await dialog.ShowDialogAsync();
            return true;
        });
    }

    // Logs and fails a request to show a dialog while another one is open. The command queue and the macOS menu bar
    // are both held while a dialog is open. A request made while a dialog is closing waits for it. A control behind
    // a dialog can still be pressed before the dialog reaches the screen, and this refuses what that press asks for.
    // Anything else that slips through becomes a diagnosable failure rather than a ContentDialog exception.
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
            contentDialog.Opened += OnDialogOpened;
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
                contentDialog.Opened -= OnDialogOpened;
                contentDialog.Closing -= OnDialogClosing;
            }

            _messengerService.Send(new ModalDialogClosedMessage());

            // Keeps the progress dialog hidden if a request is waiting to open its own dialog next. Showing it in
            // between would put two dialogs on screen at once, which throws.
            SetProgressDialogSuppressed(_requestsWaiting > 0);

            ReturnKeyboard(focusedPanel, notedFocus);

            // Done last, so a waiting dialog opens only after the keyboard has been returned.
            dialogClosed.SetResult();
        }
    }

    private void OnDialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        // The factory's focus guard handles Opened first, so the dialog holds the keyboard by now.
        _logger.LogDebug("Opened dialog '{DialogType}'", sender.GetType().Name);
    }

    private void OnDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        // A dialog that cancels its closing stays open.
        if (!args.Cancel)
        {
            OnDialogStartedClosing();
        }
    }

    // Separate from the event handler so the unit tests can call it.
    internal void OnDialogStartedClosing()
    {
        _isDialogClosing = true;
    }

    // Returns true if a new dialog can open now. If the open dialog has started to close, this waits for it to
    // finish first. If it's open and not closing, this returns false. WinUI gives the keyboard back to the control
    // that opened a dialog about 200 ms before the dialog has closed, and a key pressed on that control in that
    // time can request another dialog. A caller that gets true must open its dialog straight away.
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

        // False if the dialog stopped closing, or if another waiting request opened its dialog first.
        return !_isDialogOpen;
    }

    // Gives the keyboard back to whatever held it before the dialog opened, since closing a dialog doesn't
    // reliably do that. On the Skia heads, focus can land on the first focusable element of any panel. Even the
    // packaged Windows head can leave focus in the Explorer when a confirmation opens as the New Project dialog
    // closes. The control that held the keyboard gets it back first, unless it already has it. If the control
    // can't take it, its panel is refocused instead. This also applies to a control outside any panel, such as a
    // title bar button. A web surface still reports focus while the dialog is open, but only gets its caret back
    // when its document takes focus again, so its panel is always refocused.
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
