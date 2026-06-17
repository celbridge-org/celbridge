using Celbridge.Credentials;
using Celbridge.Packages;
using Celbridge.Settings;

namespace Celbridge.UserInterface.ViewModels.Controls;

public partial class WorkshopSettingsViewModel : ObservableObject
{
    private const string MaskedKeyDisplay = "********";

    private readonly Logging.ILogger<WorkshopSettingsViewModel> _logger;
    private readonly IEditorSettings _editorSettings;
    private readonly ICredentialService _credentialService;
    private readonly IPackageApiClient _packageApiClient;
    private readonly IStringLocalizer _stringLocalizer;

    [ObservableProperty]
    private string _workshopUrl = string.Empty;

    [ObservableProperty]
    private string _author = string.Empty;

    [ObservableProperty]
    private string _workshopKey = string.Empty;

    [ObservableProperty]
    private string _storedKeyDisplay = string.Empty;

    [ObservableProperty]
    private bool _isStoreAvailable;

    [ObservableProperty]
    private bool _isKeyEntryVisible;

    [ObservableProperty]
    private bool _isStoredKeyVisible;

    [ObservableProperty]
    private bool _isCancelReplaceVisible;

    [ObservableProperty]
    private bool _isStatusVisible;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private StatusSeverity _statusSeverity;

    private bool _isKeyStored;
    private bool _isReplacingKey;

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
        IEditorSettings editorSettings,
        ICredentialService credentialService,
        IPackageApiClient packageApiClient,
        IStringLocalizer stringLocalizer)
    {
        _logger = logger;
        _editorSettings = editorSettings;
        _credentialService = credentialService;
        _packageApiClient = packageApiClient;
        _stringLocalizer = stringLocalizer;
    }

    public async Task InitializeAsync()
    {
        IsStoreAvailable = _credentialService.IsAvailable;

        // URL and Author are ordinary settings, independent of the key store, so
        // they load (and the section displays them) even when no key is stored.
        ApplyProgrammatic(() =>
        {
            WorkshopUrl = _editorSettings.WorkshopUrl;
            Author = _editorSettings.WorkshopAuthor;
        });

        if (!IsStoreAvailable)
        {
            ShowStatus(StatusSeverity.Error, _stringLocalizer.GetString("SettingsPage_CredentialStoreUnavailable"));
            UpdateViewState();
            return;
        }

        var summaryResult = await _credentialService.GetWorkshopKeySummaryAsync();
        if (summaryResult.IsFailure)
        {
            _logger.LogError(summaryResult, "Failed to read the Workshop Key summary");
            ShowStatus(StatusSeverity.Error, _stringLocalizer.GetString("SettingsPage_StoredConnectionUnreadable"));
            UpdateViewState();
            return;
        }

        var summary = summaryResult.Value;
        _isKeyStored = summary.IsStored;
        if (_isKeyStored)
        {
            StoredKeyDisplay = FormatStoredKeyDisplay(summary.KeyHint);
        }

        UpdateViewState();

        // A stored key with no Author cannot publish; surface it up front rather
        // than waiting for the first publish to fail.
        if (_isKeyStored
            && string.IsNullOrWhiteSpace(Author))
        {
            ShowStatus(StatusSeverity.Warning, _stringLocalizer.GetString("SettingsPage_AuthorRequired"));
        }
    }

    /// <summary>
    /// Persists the current field values. The Workshop URL and Author are saved as
    /// ordinary settings, always and independently of the key. A newly entered key
    /// is saved to the credential store. When checkConnection is set and a key is
    /// stored, the connection is verified against the workshop and the result
    /// shown; the view requests a check only when a connection-affecting field
    /// changed.
    /// </summary>
    public async Task SaveWorkshopConnectionAsync(bool checkConnection = true)
    {
        if (!IsStoreAvailable)
        {
            return;
        }

        // The URL and Author are non-secret; persist them as settings on every
        // commit, so they are never coupled to the presence of a key.
        _editorSettings.WorkshopUrl = WorkshopUrl.Trim();
        _editorSettings.WorkshopAuthor = Author.Trim();

        // Persist a newly entered key to the credential store.
        var enteringKey = !_isKeyStored ||
                          _isReplacingKey;
        var keyPrefixUnexpected = false;
        if (enteringKey)
        {
            var workshopKey = WorkshopKey.Trim();
            if (!string.IsNullOrEmpty(workshopKey))
            {
                var setResult = await _credentialService.SetWorkshopKeyAsync(workshopKey);
                if (setResult.IsFailure)
                {
                    _logger.LogError(setResult, "Failed to store the Workshop Key");
                    ShowStatus(StatusSeverity.Error, _stringLocalizer.GetString("SettingsPage_SaveConnectionFailed"));
                    return;
                }

                keyPrefixUnexpected = !WorkshopConnectionValidation.HasExpectedKeyPrefix(workshopKey);
                ApplyProgrammatic(() => WorkshopKey = string.Empty);
                _isKeyStored = true;
                _isReplacingKey = false;
                await RefreshStoredKeyDisplayAsync();
                UpdateViewState();
            }
        }

        // The URL value is already persisted; validate it for the connection
        // status shown below.
        var workshopUrl = WorkshopUrl.Trim();
        if (string.IsNullOrEmpty(workshopUrl))
        {
            ShowStatus(StatusSeverity.Error, _stringLocalizer.GetString("SettingsPage_EmptyWorkshopUrl"));
            return;
        }
        if (!WorkshopConnectionValidation.IsValidWorkshopUrl(workshopUrl))
        {
            ShowStatus(StatusSeverity.Error, _stringLocalizer.GetString("SettingsPage_InvalidWorkshopUrl"));
            return;
        }

        if (!_isKeyStored)
        {
            ShowStatus(StatusSeverity.Informational, _stringLocalizer.GetString("SettingsPage_EmptyWorkshopKey"));
            return;
        }

        if (checkConnection)
        {
            await CheckConnectionAsync(keyPrefixUnexpected);
        }
        else
        {
            ShowConnectionOkStatus("SettingsPage_ConnectionSaved");
        }
    }

    // The connection is stored and (where checked) reachable. Publishing also
    // needs an Author, so a missing one is surfaced as a warning in place of the
    // success message rather than waiting for the first publish to fail.
    private void ShowConnectionOkStatus(string successMessageKey)
    {
        if (string.IsNullOrWhiteSpace(Author))
        {
            ShowStatus(StatusSeverity.Warning, _stringLocalizer.GetString("SettingsPage_AuthorRequired"));
        }
        else
        {
            ShowStatus(StatusSeverity.Success, _stringLocalizer.GetString(successMessageKey));
        }
    }

    // Verifies the connection by making one authenticated request to the
    // workshop. List-packages is reused as a lightweight probe until a dedicated
    // health endpoint exists; only its success or failure is used.
    private async Task CheckConnectionAsync(bool keyPrefixUnexpected)
    {
        var checkId = ++_connectionCheckId;
        ShowStatus(StatusSeverity.Informational, _stringLocalizer.GetString("SettingsPage_CheckingConnection"));

        var listResult = await _packageApiClient.ListPackagesAsync();

        // A newer save started its own check while this one was in flight; let
        // the newer one own the final status.
        if (checkId != _connectionCheckId)
        {
            return;
        }

        if (listResult.IsSuccess)
        {
            ShowConnectionOkStatus("SettingsPage_ConnectionVerified");
            return;
        }

        // A malformed-looking key that the workshop also rejected is reported as
        // invalid; a well-formed key that failed could be the wrong key or an
        // unreachable server, so it points at both the URL and the key.
        var messageKey = keyPrefixUnexpected
            ? "SettingsPage_InvalidWorkshopKey"
            : "SettingsPage_ConnectionCheckFailed";

        ShowStatus(StatusSeverity.Error, _stringLocalizer.GetString(messageKey));
    }

    [RelayCommand]
    private async Task ClearWorkshopKeyAsync()
    {
        var clearResult = await _credentialService.ClearWorkshopKeyAsync();
        if (clearResult.IsFailure)
        {
            _logger.LogError(clearResult, "Failed to clear the Workshop Key");
            ShowStatus(StatusSeverity.Error, _stringLocalizer.GetString("SettingsPage_ClearWorkshopKeyFailed"));
            return;
        }

        // Only the secret is removed; the URL and Author stay as settings so a new
        // key can be entered without retyping them.
        _isKeyStored = false;
        _isReplacingKey = false;
        ApplyProgrammatic(() => WorkshopKey = string.Empty);
        StoredKeyDisplay = string.Empty;

        ShowStatus(StatusSeverity.Informational, _stringLocalizer.GetString("SettingsPage_WorkshopKeyCleared"));
        UpdateViewState();
    }

    [RelayCommand]
    private void ReplaceWorkshopKey()
    {
        _isReplacingKey = true;
        ClearStatus();
        UpdateViewState();
    }

    [RelayCommand]
    private void CancelReplaceWorkshopKey()
    {
        _isReplacingKey = false;
        ApplyProgrammatic(() => WorkshopKey = string.Empty);
        ClearStatus();
        UpdateViewState();
    }

    private async Task RefreshStoredKeyDisplayAsync()
    {
        var summaryResult = await _credentialService.GetWorkshopKeySummaryAsync();
        if (summaryResult.IsSuccess)
        {
            var summary = summaryResult.Value;
            StoredKeyDisplay = FormatStoredKeyDisplay(summary.KeyHint);
        }
    }

    private void UpdateViewState()
    {
        IsKeyEntryVisible = IsStoreAvailable &&
                            (!_isKeyStored || _isReplacingKey);
        IsStoredKeyVisible = IsStoreAvailable &&
                             _isKeyStored &&
                             !_isReplacingKey;
        IsCancelReplaceVisible = IsStoreAvailable &&
                                 _isKeyStored &&
                                 _isReplacingKey;
    }

    private void ShowStatus(StatusSeverity severity, string message)
    {
        StatusSeverity = severity;
        StatusMessage = message;
        IsStatusVisible = true;
    }

    private void ClearStatus()
    {
        IsStatusVisible = false;
        StatusMessage = string.Empty;
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
