using Celbridge.Commands;
using Celbridge.WebHost;

namespace Celbridge.UserInterface.ViewModels.Controls;

public partial class PrivacySettingsViewModel : ObservableObject
{
    private readonly Logging.ILogger<PrivacySettingsViewModel> _logger;
    private readonly ICommandService _commandService;
    private readonly IStringLocalizer _stringLocalizer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClearEnabled))]
    private bool _isClearing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClearButtonVisible))]
    private bool _isConfirmingClear;

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

    /// <summary>
    /// True when the Clear button is showing. It gives up the row to the confirmation while that is up.
    /// </summary>
    public bool IsClearButtonVisible => !IsConfirmingClear;

    public PrivacySettingsViewModel(
        Logging.ILogger<PrivacySettingsViewModel> logger,
        ICommandService commandService,
        IWebViewService webViewService,
        IStringLocalizer stringLocalizer)
    {
        _logger = logger;
        _commandService = commandService;
        _stringLocalizer = stringLocalizer;

        IsClearAvailable = webViewService.CanClearBrowsingData;
        if (!IsClearAvailable)
        {
            ShowStatus(StatusSeverity.Informational, _stringLocalizer.GetString("Settings_Privacy_ClearUnavailable"));
        }
    }

    // Shows the confirmation in place of the Clear button. The clear cannot be undone, so it is confirmed
    // rather than run on the first click.
    [RelayCommand]
    private void BeginClearBrowsingData()
    {
        if (!IsClearEnabled)
        {
            return;
        }

        IsConfirmingClear = true;
    }

    [RelayCommand]
    private void CancelClearBrowsingData()
    {
        IsConfirmingClear = false;
    }

    // Runs the clear. Bound to the confirmation's Clear button.
    [RelayCommand]
    private async Task ConfirmClearBrowsingDataAsync()
    {
        if (!IsClearEnabled)
        {
            return;
        }

        IsConfirmingClear = false;
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
