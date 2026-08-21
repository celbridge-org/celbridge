using Celbridge.Packages;
using Celbridge.Settings;

namespace Celbridge.UserInterface.ViewModels.Controls;

public partial class WorkshopSettingsViewModel : ObservableObject
{
    private const string MaskedKeyDisplay = "********";

    private readonly Logging.ILogger<WorkshopSettingsViewModel> _logger;
    private readonly ISettingsService _settingsService;
    private readonly IPackageApiClient _packageApiClient;
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
    private bool _isKeyEditVisible;

    [ObservableProperty]
    private bool _isRemoveConfirmVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaveKeyEnabled))]
    private string _keyInput = string.Empty;

    [ObservableProperty]
    private bool _isStatusVisible;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private StatusSeverity _statusSeverity;

    private bool _isKeyStored;

    // The key row shows one of four things: the Set button, the stored hint with Change and Remove, the
    // entry field, or the removal confirmation. These two pick between them.
    private bool _isEditingKey;
    private bool _isConfirmingRemove;

    // Bumped on each connection check so the result of a slow check that is
    // superseded by a newer save does not overwrite the newer status.
    private int _connectionCheckId;

    /// <summary>
    /// True while the view model is updating bound fields itself (load, clear,
    /// post-save reset), so the view can tell a programmatic change from a user
    /// edit and not trigger an auto-save.
    /// </summary>
    public bool IsApplyingProgrammaticChange { get; private set; }

    /// <summary>
    /// True when the entered key can be saved. A blank field has nothing to store.
    /// </summary>
    public bool IsSaveKeyEnabled => !string.IsNullOrWhiteSpace(KeyInput);

    public WorkshopSettingsViewModel(
        Logging.ILogger<WorkshopSettingsViewModel> logger,
        ISettingsService settingsService,
        IPackageApiClient packageApiClient,
        IStringLocalizer stringLocalizer)
    {
        _logger = logger;
        _settingsService = settingsService;
        _packageApiClient = packageApiClient;
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
            WorkshopUrl = _settingsService.Get(SettingCatalog.Workshop.Url);
            Author = _settingsService.Get(SettingCatalog.Workshop.Author);
        });

        if (!IsStoreAvailable)
        {
            ShowStatus(StatusSeverity.Error, _stringLocalizer.GetString("Settings_Workshop_StoreUnavailable"));
            UpdateViewState();
            return;
        }

        // Read the stored state and display hint without decrypting the key.
        _isKeyStored = _settingsService.IsConfigured(SettingCatalog.Workshop.Key);
        if (_isKeyStored)
        {
            StoredKeyDisplay = FormatStoredKeyDisplay(_settingsService.Get(SettingCatalog.Workshop.KeyHint));
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
    /// Persists the non-secret Workshop URL and Author as ordinary settings. This is the auto-save path for
    /// field edits and does not verify the connection; the user tests the connection explicitly through
    /// TestConnection. The Workshop Key is entered separately through the key row.
    /// </summary>
    public void SaveWorkshopConnection()
    {
        if (!IsStoreAvailable)
        {
            return;
        }

        // The URL and Author are non-secret; persist them as settings, so they are never coupled to the
        // presence of a key.
        _settingsService.Set(SettingCatalog.Workshop.Url, WorkshopUrl.Trim());
        _settingsService.Set(SettingCatalog.Workshop.Author, Author.Trim());
    }

    // Persists the current field values and verifies the connection against the workshop, reporting the
    // outcome in the status bar. Bound to the Test Connection button.
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (!IsStoreAvailable)
        {
            return;
        }

        // Persist first so the probe tests exactly the URL and Author shown in the fields.
        SaveWorkshopConnection();

        await ReportConnectionStatusAsync();
    }

    // Shows the key entry field in place of the key row. Bound to both the Set and Change buttons.
    [RelayCommand]
    private void BeginChangeWorkshopKey()
    {
        if (!IsStoreAvailable)
        {
            return;
        }

        KeyInput = string.Empty;
        _isConfirmingRemove = false;
        _isEditingKey = true;
        UpdateViewState();
    }

    [RelayCommand]
    private void CancelChangeWorkshopKey()
    {
        KeyInput = string.Empty;
        _isEditingKey = false;
        UpdateViewState();
    }

    [RelayCommand]
    private async Task SaveWorkshopKeyAsync()
    {
        if (!IsStoreAvailable)
        {
            return;
        }

        var workshopKey = KeyInput.Trim();
        if (string.IsNullOrEmpty(workshopKey))
        {
            return;
        }

        var setResult = StoreWorkshopKey(workshopKey);
        if (setResult.IsFailure)
        {
            // The field keeps what was typed so the user can try again.
            _logger.LogError(setResult, "Failed to store the Workshop Key");
            ShowStatus(StatusSeverity.Error, _stringLocalizer.GetString("Settings_Workshop_ConnectionSaveFailed"));
            return;
        }

        KeyInput = string.Empty;
        _isEditingKey = false;
        _isKeyStored = true;
        RefreshStoredKeyDisplay();
        UpdateViewState();

        await ReportConnectionStatusAsync();
    }

    // Shows the removal confirmation in place of the key row. A removed key cannot be recovered, so it is
    // confirmed rather than done on the first click.
    [RelayCommand]
    private void BeginRemoveWorkshopKey()
    {
        if (!IsStoreAvailable)
        {
            return;
        }

        _isEditingKey = false;
        _isConfirmingRemove = true;
        UpdateViewState();
    }

    [RelayCommand]
    private void CancelRemoveWorkshopKey()
    {
        _isConfirmingRemove = false;
        UpdateViewState();
    }

    [RelayCommand]
    private void ConfirmRemoveWorkshopKey()
    {
        ClearWorkshopKey();

        // Only the secret is removed; the URL and Author stay as settings so a new
        // key can be entered without retyping them.
        _isKeyStored = false;
        _isConfirmingRemove = false;
        StoredKeyDisplay = string.Empty;

        ShowStatus(StatusSeverity.Informational, _stringLocalizer.GetString("Settings_Workshop_KeyRemoved"));
        UpdateViewState();
    }

    // Validates the URL and, when a key is stored, verifies the connection against the workshop.
    private async Task ReportConnectionStatusAsync()
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

        await CheckConnectionAsync();
    }

    // The connection is stored and reachable. Publishing also needs an Author, so a missing one is
    // surfaced as a warning in place of the success message rather than waiting for the first publish
    // to fail.
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
        StoredKeyDisplay = FormatStoredKeyDisplay(_settingsService.Get(SettingCatalog.Workshop.KeyHint));
    }

    // Encrypts and stores the Workshop Key, and records its non-secret display
    // hint alongside. The key is written before the hint, so a failure between
    // the two leaves a usable key with a stale hint rather than a hint with no key.
    private Result StoreWorkshopKey(string workshopKey)
    {
        try
        {
            _settingsService.Set(SettingCatalog.Workshop.Key, workshopKey);
            _settingsService.Set(SettingCatalog.Workshop.KeyHint, WorkshopKeyHelper.GetDisplayHint(workshopKey));
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail("Failed to store the Workshop Key").WithException(ex);
        }

        return Result.Ok();
    }

    private void ClearWorkshopKey()
    {
        _settingsService.Reset(SettingCatalog.Workshop.Key);
        _settingsService.Reset(SettingCatalog.Workshop.KeyHint);
    }

    private void UpdateViewState()
    {
        IsKeyEditVisible = IsStoreAvailable &&
                           _isEditingKey;
        IsRemoveConfirmVisible = IsStoreAvailable &&
                                 _isConfirmingRemove;

        // The Set button and the stored hint are the resting states, shown only when neither the entry
        // field nor the removal confirmation has taken the row.
        bool showKeyRow = IsStoreAvailable &&
                          !_isEditingKey &&
                          !_isConfirmingRemove;

        IsSetKeyVisible = showKeyRow &&
                          !_isKeyStored;
        IsStoredKeyVisible = showKeyRow &&
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
