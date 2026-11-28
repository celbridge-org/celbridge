using Celbridge.Settings;

namespace Celbridge.UserInterface.ViewModels.Controls;

/// <summary>
/// The state of a SecretField, which stores one secret in a Protected setting. The field never reads the
/// secret back, and shows only whether a secret is stored.
/// </summary>
public partial class SecretFieldViewModel : ObservableObject
{
    private const string MaskedSecretDisplay = "********";

    private readonly Logging.ILogger<SecretFieldViewModel> _logger;
    private readonly ISettingsService _settingsService;
    private readonly IStringLocalizer _stringLocalizer;

    private SettingDescriptor<string>? _setting;

    [ObservableProperty]
    private bool _isStoreAvailable;

    [ObservableProperty]
    private bool _isSetVisible;

    [ObservableProperty]
    private bool _isStoredVisible;

    [ObservableProperty]
    private bool _isEditVisible;

    [ObservableProperty]
    private bool _isRemoveConfirmVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaveEnabled))]
    private string _secretInput = string.Empty;

    [ObservableProperty]
    private bool _isStatusVisible;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private StatusSeverity _statusSeverity;

    private bool _isSecretStored;

    // True while the entry field or the removal confirmation shows.
    private bool _isEditing;
    private bool _isConfirmingRemove;

    /// <summary>
    /// The text the field shows in place of a stored secret.
    /// </summary>
    public string StoredSecretDisplay => MaskedSecretDisplay;

    /// <summary>
    /// True when the entered secret can be saved. A blank field has nothing to store.
    /// </summary>
    public bool IsSaveEnabled => !string.IsNullOrWhiteSpace(SecretInput);

    public SecretFieldViewModel(
        Logging.ILogger<SecretFieldViewModel> logger,
        ISettingsService settingsService,
        IStringLocalizer stringLocalizer)
    {
        _logger = logger;
        _settingsService = settingsService;
        _stringLocalizer = stringLocalizer;
    }

    /// <summary>
    /// Points the field at a Protected setting, and loads whether a secret is stored in the setting.
    /// </summary>
    public void Initialize(SettingDescriptor<string> setting)
    {
        if (setting.Scope != SettingScope.Protected)
        {
            throw new ArgumentException($"A secret field stores only a Protected setting, and '{setting.Key}' has another scope", nameof(setting));
        }

        _setting = setting;
        _isEditing = false;
        _isConfirmingRemove = false;
        SecretInput = string.Empty;
        IsStatusVisible = false;

        IsStoreAvailable = _settingsService.IsScopeAvailable(SettingScope.Protected);
        if (!IsStoreAvailable)
        {
            _isSecretStored = false;
            ShowStatus(StatusSeverity.Error, _stringLocalizer.GetString("SecretField_StoreUnavailable"));
            UpdateViewState();
            return;
        }

        // The settings service answers this without reading or decrypting the secret.
        _isSecretStored = _settingsService.IsConfigured(setting);
        UpdateViewState();
    }

    // Shows the entry field in place of the Set button or the mask. Bound to both the Set and Change buttons.
    [RelayCommand]
    private void BeginChange()
    {
        if (!IsStoreAvailable)
        {
            return;
        }

        SecretInput = string.Empty;
        _isConfirmingRemove = false;
        _isEditing = true;
        IsStatusVisible = false;
        UpdateViewState();
    }

    [RelayCommand]
    private void CancelChange()
    {
        SecretInput = string.Empty;
        _isEditing = false;
        IsStatusVisible = false;
        UpdateViewState();
    }

    [RelayCommand]
    private void Save()
    {
        if (!IsStoreAvailable
            || _setting is null)
        {
            return;
        }

        var secret = SecretInput.Trim();
        if (string.IsNullOrEmpty(secret))
        {
            return;
        }

        var storeResult = StoreSecret(_setting, secret);
        if (storeResult.IsFailure)
        {
            // The field keeps what was typed, so the user can try again.
            _logger.LogError(storeResult, $"Failed to store the secret for '{_setting.Key}'");
            ShowStatus(StatusSeverity.Error, _stringLocalizer.GetString("SecretField_SaveFailed"));
            return;
        }

        SecretInput = string.Empty;
        _isEditing = false;
        _isSecretStored = true;
        IsStatusVisible = false;
        UpdateViewState();
    }

    // Shows the removal confirmation in place of the mask. A removed secret is gone for good, so the user
    // confirms the removal first.
    [RelayCommand]
    private void BeginRemove()
    {
        if (!IsStoreAvailable)
        {
            return;
        }

        _isEditing = false;
        _isConfirmingRemove = true;
        IsStatusVisible = false;
        UpdateViewState();
    }

    [RelayCommand]
    private void CancelRemove()
    {
        _isConfirmingRemove = false;
        IsStatusVisible = false;
        UpdateViewState();
    }

    [RelayCommand]
    private void ConfirmRemove()
    {
        if (_setting is null)
        {
            return;
        }

        _settingsService.Reset(_setting);

        // Reset reports no failure, so check whether the secret is gone.
        _isSecretStored = _settingsService.IsConfigured(_setting);
        _isConfirmingRemove = false;

        if (_isSecretStored)
        {
            _logger.LogError($"Failed to remove the secret for '{_setting.Key}'");
            ShowStatus(StatusSeverity.Error, _stringLocalizer.GetString("SecretField_RemoveFailed"));
        }
        else
        {
            IsStatusVisible = false;
        }

        UpdateViewState();
    }

    private Result StoreSecret(SettingDescriptor<string> setting, string secret)
    {
        try
        {
            _settingsService.Set(setting, secret);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail($"Failed to store the secret for '{setting.Key}'").WithException(ex);
        }

        return Result.Ok();
    }

    private void UpdateViewState()
    {
        IsEditVisible = IsStoreAvailable
            && _isEditing;
        IsRemoveConfirmVisible = IsStoreAvailable
            && _isConfirmingRemove;

        // The Set button and the mask are the resting states. One of them shows while the entry field and the
        // removal confirmation are both hidden.
        bool isResting = IsStoreAvailable
            && !_isEditing
            && !_isConfirmingRemove;

        IsSetVisible = isResting
            && !_isSecretStored;
        IsStoredVisible = isResting
            && _isSecretStored;
    }

    private void ShowStatus(StatusSeverity severity, string message)
    {
        StatusSeverity = severity;
        StatusMessage = message;
        IsStatusVisible = true;
    }
}
