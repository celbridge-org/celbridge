using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.WebHost;

namespace Celbridge.UserInterface.ViewModels.Controls;

public partial class PrivacySettingsViewModel : ObservableObject
{
    private readonly Logging.ILogger<PrivacySettingsViewModel> _logger;
    private readonly ICommandService _commandService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClearEnabled))]
    private bool _isClearing;

    [ObservableProperty]
    private bool _isStatusVisible;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private StatusSeverity _statusSeverity;

    /// <summary>
    /// True when this platform can clear browsing data at all. Fixed for the session, so there is nothing
    /// to refresh when the settings page is displayed.
    /// </summary>
    public bool IsClearAvailable { get; }

    /// <summary>
    /// True when the clear action can be started: the platform supports clearing and no clear is running.
    /// </summary>
    public bool IsClearEnabled => IsClearAvailable && !IsClearing;

    public PrivacySettingsViewModel(
        Logging.ILogger<PrivacySettingsViewModel> logger,
        ICommandService commandService,
        IWebViewService webViewService,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer)
    {
        _logger = logger;
        _commandService = commandService;
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;

        IsClearAvailable = webViewService.CanClearBrowsingData;
        if (!IsClearAvailable)
        {
            ShowStatus(StatusSeverity.Informational, _stringLocalizer.GetString("Settings_Privacy_ClearUnavailable"));
        }
    }

    // Confirms the clear with the user and, on acceptance, runs it. Bound to the Clear Browsing Data button.
    [RelayCommand]
    private async Task ConfirmClearBrowsingDataAsync()
    {
        if (!IsClearEnabled)
        {
            return;
        }

        var title = _stringLocalizer.GetString("Settings_Privacy_ClearDialogTitle");
        var message = _stringLocalizer.GetString("Settings_Privacy_ClearDialogMessage");
        var confirmButtonText = _stringLocalizer.GetString("Settings_Privacy_ClearConfirmButton");

        var confirmOptions = new ConfirmationDialogOptions
        {
            PrimaryButtonText = confirmButtonText,
            IsDestructive = true
        };

        var confirmResult = await _dialogService.ShowConfirmationDialogAsync(title, message, confirmOptions);
        if (confirmResult.IsFailure
            || !confirmResult.Value)
        {
            return;
        }

        IsClearing = true;
        ShowStatus(StatusSeverity.Informational, _stringLocalizer.GetString("Settings_Privacy_Clearing"));

        var clearResult = await _commandService.ExecuteAsync<IClearBrowsingDataCommand>();

        IsClearing = false;

        if (clearResult.IsFailure)
        {
            _logger.LogError(clearResult, "Failed to clear the browsing data");
            ShowStatus(StatusSeverity.Error, _stringLocalizer.GetString("Settings_Privacy_ClearFailed"));
            return;
        }

        ShowStatus(StatusSeverity.Success, _stringLocalizer.GetString("Settings_Privacy_Cleared"));
    }

    private void ShowStatus(StatusSeverity severity, string message)
    {
        StatusSeverity = severity;
        StatusMessage = message;
        IsStatusVisible = true;
    }
}
