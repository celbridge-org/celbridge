using Celbridge.Credentials;

namespace Celbridge.UserInterface.ViewModels.Pages;

public partial class SettingsPageViewModel : ObservableObject
{
    private const string MaskedKeyDisplay = "********";

    private readonly Logging.ILogger<SettingsPageViewModel> _logger;
    private readonly ICredentialService _credentialService;
    private readonly IStringLocalizer _stringLocalizer;

    [ObservableProperty]
    private string _workshopUrl = string.Empty;

    [ObservableProperty]
    private string _applicationKey = string.Empty;

    [ObservableProperty]
    private string _storedKeyDisplay = string.Empty;

    [ObservableProperty]
    private bool _isStoreAvailable;

    [ObservableProperty]
    private bool _isKeyEntryVisible;

    [ObservableProperty]
    private bool _isStoredKeyVisible;

    [ObservableProperty]
    private bool _isClearVisible;

    [ObservableProperty]
    private bool _isCancelReplaceVisible;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isStatusVisible;

    [ObservableProperty]
    private bool _isWarningVisible;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _isErrorVisible;

    private bool _isConnectionStored;
    private bool _isReplacingKey;

    public SettingsPageViewModel(
        Logging.ILogger<SettingsPageViewModel> logger,
        ICredentialService credentialService,
        IStringLocalizer stringLocalizer)
    {
        _logger = logger;
        _credentialService = credentialService;
        _stringLocalizer = stringLocalizer;
    }

    public async Task InitializeAsync()
    {
        IsStoreAvailable = _credentialService.IsAvailable;
        if (!IsStoreAvailable)
        {
            ShowError(_stringLocalizer.GetString("SettingsPage_CredentialStoreUnavailable"));
            UpdateViewState();
            return;
        }

        var summaryResult = await _credentialService.GetWorkshopConnectionSummaryAsync();
        if (summaryResult.IsFailure)
        {
            _logger.LogError(summaryResult, "Failed to read the Workshop connection summary");
            ShowError(_stringLocalizer.GetString("SettingsPage_StoredConnectionUnreadable"));
            UpdateViewState();
            return;
        }

        var summary = summaryResult.Value;
        _isConnectionStored = summary.IsStored;

        if (_isConnectionStored)
        {
            StoredKeyDisplay = FormatStoredKeyDisplay(summary.KeyHint);

            var getResult = await _credentialService.GetWorkshopConnectionAsync();
            if (getResult.IsSuccess)
            {
                var connection = getResult.Value;
                WorkshopUrl = connection.WorkshopUrl;
            }
            else
            {
                // A stored entry that cannot be decrypted. The clear and
                // replace affordances stay active so the user can recover.
                _logger.LogError(getResult, "Failed to read the stored Workshop connection");
                ShowError(_stringLocalizer.GetString("SettingsPage_StoredConnectionUnreadable"));
            }
        }

        UpdateViewState();
    }

    [RelayCommand]
    private async Task SaveWorkshopConnectionAsync()
    {
        ClearMessages();

        var workshopUrl = WorkshopUrl.Trim();
        if (!WorkshopConnectionValidation.IsValidWorkshopUrl(workshopUrl))
        {
            ShowError(_stringLocalizer.GetString("SettingsPage_InvalidWorkshopUrl"));
            return;
        }

        string applicationKey;
        var isNewKey = !_isConnectionStored ||
                       _isReplacingKey;
        if (isNewKey)
        {
            applicationKey = ApplicationKey.Trim();
            if (string.IsNullOrEmpty(applicationKey))
            {
                ShowError(_stringLocalizer.GetString("SettingsPage_EmptyApplicationKey"));
                return;
            }
        }
        else
        {
            // A URL-only update reuses the stored key, read at the point of use.
            var getResult = await _credentialService.GetWorkshopConnectionAsync();
            if (getResult.IsFailure)
            {
                _logger.LogError(getResult, "Failed to read the stored Workshop connection");
                ShowError(_stringLocalizer.GetString("SettingsPage_StoredConnectionUnreadable"));
                return;
            }

            var storedConnection = getResult.Value;
            applicationKey = storedConnection.ApplicationKey;
        }

        var connection = new WorkshopConnection(workshopUrl, applicationKey);
        var setResult = await _credentialService.SetWorkshopConnectionAsync(connection);
        if (setResult.IsFailure)
        {
            _logger.LogError(setResult, "Failed to store the Workshop connection");
            ShowError(_stringLocalizer.GetString("SettingsPage_SaveConnectionFailed"));
            return;
        }

        ApplicationKey = string.Empty;
        _isConnectionStored = true;
        _isReplacingKey = false;

        await RefreshStoredKeyDisplayAsync();

        // The prefix check is a typo guard, not a gate: the connection is
        // saved either way and the warning invites a double check.
        if (isNewKey &&
            !WorkshopConnectionValidation.HasExpectedKeyPrefix(applicationKey))
        {
            ShowWarning(_stringLocalizer.GetString("SettingsPage_ConnectionSavedPrefixWarning"));
        }
        else
        {
            ShowStatus(_stringLocalizer.GetString("SettingsPage_ConnectionSaved"));
        }

        UpdateViewState();
    }

    [RelayCommand]
    private async Task ClearWorkshopConnectionAsync()
    {
        ClearMessages();

        var clearResult = await _credentialService.ClearWorkshopConnectionAsync();
        if (clearResult.IsFailure)
        {
            _logger.LogError(clearResult, "Failed to clear the Workshop connection");
            ShowError(_stringLocalizer.GetString("SettingsPage_ClearConnectionFailed"));
            return;
        }

        _isConnectionStored = false;
        _isReplacingKey = false;
        WorkshopUrl = string.Empty;
        ApplicationKey = string.Empty;
        StoredKeyDisplay = string.Empty;

        ShowStatus(_stringLocalizer.GetString("SettingsPage_ConnectionCleared"));
        UpdateViewState();
    }

    [RelayCommand]
    private void ReplaceApplicationKey()
    {
        ClearMessages();
        _isReplacingKey = true;
        UpdateViewState();
    }

    [RelayCommand]
    private void CancelReplaceApplicationKey()
    {
        ClearMessages();
        _isReplacingKey = false;
        ApplicationKey = string.Empty;
        UpdateViewState();
    }

    private async Task RefreshStoredKeyDisplayAsync()
    {
        var summaryResult = await _credentialService.GetWorkshopConnectionSummaryAsync();
        if (summaryResult.IsSuccess)
        {
            var summary = summaryResult.Value;
            StoredKeyDisplay = FormatStoredKeyDisplay(summary.KeyHint);
        }
    }

    private void UpdateViewState()
    {
        IsKeyEntryVisible = IsStoreAvailable &&
                            (!_isConnectionStored || _isReplacingKey);
        IsStoredKeyVisible = IsStoreAvailable &&
                             _isConnectionStored &&
                             !_isReplacingKey;
        IsClearVisible = IsStoreAvailable &&
                         _isConnectionStored;
        IsCancelReplaceVisible = IsStoreAvailable &&
                                 _isConnectionStored &&
                                 _isReplacingKey;
    }

    private void ShowStatus(string statusText)
    {
        StatusText = statusText;
        IsStatusVisible = true;
    }

    private void ShowWarning(string warningText)
    {
        StatusText = warningText;
        IsWarningVisible = true;
    }

    private void ShowError(string errorText)
    {
        ErrorText = errorText;
        IsErrorVisible = true;
    }

    private void ClearMessages()
    {
        StatusText = string.Empty;
        IsStatusVisible = false;
        IsWarningVisible = false;
        ErrorText = string.Empty;
        IsErrorVisible = false;
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
