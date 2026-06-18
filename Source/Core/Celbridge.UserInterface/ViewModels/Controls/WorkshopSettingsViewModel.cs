using Celbridge.Dialog;
using Celbridge.Packages;
using Celbridge.Settings;

namespace Celbridge.UserInterface.ViewModels.Controls;

public partial class WorkshopSettingsViewModel : ObservableObject
{
    private const string MaskedKeyDisplay = "********";

    private readonly Logging.ILogger<WorkshopSettingsViewModel> _logger;
    private readonly ISettingsService _settingsService;
    private readonly IPackageApiClient _packageApiClient;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    [ObservableProperty]
    private string _workshopUrl = string.Empty;

    [ObservableProperty]
    private string _author = string.Empty;

    [ObservableProperty]
    private string _storedKeyDisplay = string.Empty;

    [ObservableProperty]
    private bool _isStoreAvailable;

    [ObservableProperty]
    private bool _isSetKeyVisible;

    [ObservableProperty]
    private bool _isStoredKeyVisible;

    [ObservableProperty]
    private bool _isStatusVisible;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private StatusSeverity _statusSeverity;

    private bool _isKeyStored;

    // Bumped on each connection check so the result of a slow check that is
    // superseded by a newer save does not overwrite the newer status.
    private int _connectionCheckId;

    /// <summary>
    /// True while the view model is updating bound fields itself (load, clear,
    /// post-save reset), so the view can tell a programmatic change from a user
    /// edit and not trigger an auto-save.
    /// </summary>
    public bool IsApplyingProgrammaticChange { get; private set; }

    public WorkshopSettingsViewModel(
        Logging.ILogger<WorkshopSettingsViewModel> logger,
        ISettingsService settingsService,
        IPackageApiClient packageApiClient,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer)
    {
        _logger = logger;
        _settingsService = settingsService;
        _packageApiClient = packageApiClient;
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;
    }

    public async Task InitializeAsync()
    {
        await Task.CompletedTask;

        IsStoreAvailable = _settingsService.IsScopeAvailable(SettingScope.Protected);

        // URL and Author are ordinary settings, independent of the key store, so
        // they load (and the section displays them) even when no key is stored.
        ApplyProgrammatic(() =>
        {
            WorkshopUrl = _settingsService.Get(Setting.Workshop.Url);
            Author = _settingsService.Get(Setting.Workshop.Author);
        });

        if (!IsStoreAvailable)
        {
            ShowStatus(StatusSeverity.Error, _stringLocalizer.GetString("Settings_Workshop_StoreUnavailable"));
            UpdateViewState();
            return;
        }

        // Read the stored state and display hint without decrypting the key.
        _isKeyStored = _settingsService.IsConfigured(Setting.Workshop.Key);
        if (_isKeyStored)
        {
            StoredKeyDisplay = FormatStoredKeyDisplay(_settingsService.Get(Setting.Workshop.KeyHint));
        }

        UpdateViewState();

        // A stored key with no Author cannot publish; surface it up front rather
        // than waiting for the first publish to fail.
        if (_isKeyStored
            && string.IsNullOrWhiteSpace(Author))
        {
            ShowStatus(StatusSeverity.Warning, _stringLocalizer.GetString("Settings_Workshop_AuthorRequired"));
        }
    }

    /// <summary>
    /// Persists the non-secret Workshop URL and Author as ordinary settings and
    /// reports the resulting connection status. The Workshop Key is not handled
    /// here; it is entered through ChangeWorkshopKey. When checkConnection is set
    /// and a key is stored, the connection is verified against the workshop; the
    /// view requests a check only when a connection-affecting field changed.
    /// </summary>
    public async Task SaveWorkshopConnectionAsync(bool checkConnection = true)
    {
        if (!IsStoreAvailable)
        {
            return;
        }

        // The URL and Author are non-secret; persist them as settings on every
        // commit, so they are never coupled to the presence of a key.
        _settingsService.Set(Setting.Workshop.Url, WorkshopUrl.Trim());
        _settingsService.Set(Setting.Workshop.Author, Author.Trim());

        await ReportConnectionStatusAsync(checkConnection);
    }

    [RelayCommand]
    private async Task ChangeWorkshopKeyAsync()
    {
        if (!IsStoreAvailable)
        {
            return;
        }

        var titleKey = _isKeyStored
            ? "Settings_Workshop_KeyChangeDialogTitle"
            : "Settings_Workshop_KeySetDialogTitle";
        var title = _stringLocalizer.GetString(titleKey);
        var header = _stringLocalizer.GetString("Settings_Workshop_KeyTooltip");

        var inputResult = await _dialogService.ShowSecretInputDialogAsync(title, header, "Settings_Workshop_KeySaveButton");
        if (inputResult.IsFailure)
        {
            // The user cancelled the dialog; leave any stored key untouched.
            return;
        }

        var workshopKey = inputResult.Value.Trim();
        if (string.IsNullOrEmpty(workshopKey))
        {
            return;
        }

        var setResult = StoreWorkshopKey(workshopKey);
        if (setResult.IsFailure)
        {
            _logger.LogError(setResult, "Failed to store the Workshop Key");
            ShowStatus(StatusSeverity.Error, _stringLocalizer.GetString("Settings_Workshop_ConnectionSaveFailed"));
            return;
        }

        _isKeyStored = true;
        RefreshStoredKeyDisplay();
        UpdateViewState();

        await ReportConnectionStatusAsync(checkConnection: true);
    }

    [RelayCommand]
    private async Task RemoveWorkshopKeyAsync()
    {
        var title = _stringLocalizer.GetString("Settings_Workshop_KeyRemoveTitle");
        var message = _stringLocalizer.GetString("Settings_Workshop_KeyRemoveMessage");
        var confirmResult = await _dialogService.ShowConfirmationDialogAsync(title, message);
        if (confirmResult.IsFailure
            || !confirmResult.Value)
        {
            return;
        }

        ClearWorkshopKey();

        // Only the secret is removed; the URL and Author stay as settings so a new
        // key can be entered without retyping them.
        _isKeyStored = false;
        StoredKeyDisplay = string.Empty;

        ShowStatus(StatusSeverity.Informational, _stringLocalizer.GetString("Settings_Workshop_KeyRemoved"));
        UpdateViewState();
    }

    // Validates the URL and reports the connection status. When a key is stored
    // and checkConnection is set, the connection is verified against the workshop.
    private async Task ReportConnectionStatusAsync(bool checkConnection)
    {
        var workshopUrl = WorkshopUrl.Trim();
        if (string.IsNullOrEmpty(workshopUrl))
        {
            ShowStatus(StatusSeverity.Error, _stringLocalizer.GetString("Settings_Workshop_UrlEmpty"));
            return;
        }
        if (!WorkshopConnectionValidation.IsValidWorkshopUrl(workshopUrl))
        {
            ShowStatus(StatusSeverity.Error, _stringLocalizer.GetString("Settings_Workshop_UrlInvalid"));
            return;
        }

        if (!_isKeyStored)
        {
            ShowStatus(StatusSeverity.Informational, _stringLocalizer.GetString("Settings_Workshop_KeyEmpty"));
            return;
        }

        if (checkConnection)
        {
            await CheckConnectionAsync();
        }
        else
        {
            ShowConnectionOkStatus("Settings_Workshop_ConnectionSaved");
        }
    }

    // The connection is stored and (where checked) reachable. Publishing also
    // needs an Author, so a missing one is surfaced as a warning in place of the
    // success message rather than waiting for the first publish to fail.
    private void ShowConnectionOkStatus(string successMessageKey)
    {
        if (string.IsNullOrWhiteSpace(Author))
        {
            ShowStatus(StatusSeverity.Warning, _stringLocalizer.GetString("Settings_Workshop_AuthorRequired"));
        }
        else
        {
            ShowStatus(StatusSeverity.Success, _stringLocalizer.GetString(successMessageKey));
        }
    }

    // Classifies the workshop connection from a single authenticated probe and
    // reports it: verified, key rejected, or saved-but-unverified when the
    // workshop could not be reached.
    private async Task CheckConnectionAsync()
    {
        var checkId = ++_connectionCheckId;
        ShowStatus(StatusSeverity.Informational, _stringLocalizer.GetString("Settings_Workshop_ConnectionChecking"));

        var outcome = await _packageApiClient.CheckConnectionAsync();

        // A newer save started its own check while this one was in flight; let
        // the newer one own the final status.
        if (checkId != _connectionCheckId)
        {
            return;
        }

        switch (outcome)
        {
            case ConnectionCheckOutcome.Connected:
                ShowConnectionOkStatus("Settings_Workshop_ConnectionVerified");
                break;

            case ConnectionCheckOutcome.Unauthorized:
                // The workshop definitively rejected the key, so name the key.
                ShowStatus(StatusSeverity.Error, _stringLocalizer.GetString("Settings_Workshop_KeyRejected"));
                break;

            case ConnectionCheckOutcome.Unreachable:
                // The key is stored; we just could not verify it right now, so
                // report a warning rather than claiming the key is wrong.
                ShowStatus(StatusSeverity.Warning, _stringLocalizer.GetString("Settings_Workshop_ConnectionUnverified"));
                break;
        }
    }

    private void RefreshStoredKeyDisplay()
    {
        StoredKeyDisplay = FormatStoredKeyDisplay(_settingsService.Get(Setting.Workshop.KeyHint));
    }

    // Encrypts and stores the Workshop Key, and records its non-secret display
    // hint alongside. The key is written before the hint, so a failure between
    // the two leaves a usable key with a stale hint rather than a hint with no key.
    private Result StoreWorkshopKey(string workshopKey)
    {
        try
        {
            _settingsService.Set(Setting.Workshop.Key, workshopKey);
            _settingsService.Set(Setting.Workshop.KeyHint, WorkshopKey.GetDisplayHint(workshopKey));
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail("Failed to store the Workshop Key").WithException(ex);
        }

        return Result.Ok();
    }

    private void ClearWorkshopKey()
    {
        _settingsService.Reset(Setting.Workshop.Key);
        _settingsService.Reset(Setting.Workshop.KeyHint);
    }

    private void UpdateViewState()
    {
        IsSetKeyVisible = IsStoreAvailable &&
                          !_isKeyStored;
        IsStoredKeyVisible = IsStoreAvailable &&
                             _isKeyStored;
    }

    private void ShowStatus(StatusSeverity severity, string message)
    {
        StatusSeverity = severity;
        StatusMessage = message;
        IsStatusVisible = true;
    }

    // Runs an update to bound fields with the programmatic-change flag set, so the
    // view's auto-save trigger ignores changes the view model makes itself.
    private void ApplyProgrammatic(Action action)
    {
        IsApplyingProgrammaticChange = true;
        try
        {
            action();
        }
        finally
        {
            IsApplyingProgrammaticChange = false;
        }
    }

    private static string FormatStoredKeyDisplay(string keyHint)
    {
        if (string.IsNullOrEmpty(keyHint))
        {
            return MaskedKeyDisplay;
        }

        return $"{keyHint}_...";
    }
}
