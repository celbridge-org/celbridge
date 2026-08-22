using System.ComponentModel;
using Celbridge.Logging;
using Celbridge.UserInterface.ViewModels.Controls;
using Microsoft.UI.Dispatching;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// The Workshop connection section of the settings dialog.
/// </summary>
public sealed partial class WorkshopSettingsView : UserControl
{
    private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<WorkshopSettingsView> _logger;
    private readonly IStringLocalizer _stringLocalizer;

    private DispatcherQueueTimer? _autoSaveTimer;

    private string WorkshopUrlString => _stringLocalizer.GetString("Settings_Workshop_Url");
    private string WorkshopUrlTooltipString => _stringLocalizer.GetString("Settings_Workshop_UrlTooltip");
    private string WorkshopKeyString => _stringLocalizer.GetString("Settings_Workshop_Key");
    private string KeyTooltipString => _stringLocalizer.GetString("Settings_Workshop_KeyTooltip");
    private string SaveKeyString => _stringLocalizer.GetString("Settings_Workshop_KeySaveButton");
    private string KeyRemoveMessageString => _stringLocalizer.GetString("Settings_Workshop_KeyRemoveMessage");
    private string CancelString => _stringLocalizer.GetString("DialogButton_Cancel");
    private string AuthorString => _stringLocalizer.GetString("Settings_Workshop_Author");
    private string AuthorTooltipString => _stringLocalizer.GetString("Settings_Workshop_AuthorTooltip");
    private string AuthorPlaceholderString => _stringLocalizer.GetString("Settings_Workshop_AuthorPlaceholder");
    private string SetWorkshopKeyString => _stringLocalizer.GetString("Settings_Workshop_KeySet");
    private string ChangeKeyString => _stringLocalizer.GetString("Settings_Workshop_KeyChange");
    private string RemoveKeyString => _stringLocalizer.GetString("Settings_Workshop_KeyRemove");
    private string TestConnectionString => _stringLocalizer.GetString("Settings_Workshop_TestConnection");

    public WorkshopSettingsViewModel ViewModel { get; }

    public WorkshopSettingsView()
    {
        _logger = ServiceLocator.AcquireService<ILogger<WorkshopSettingsView>>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<WorkshopSettingsViewModel>();

        this.InitializeComponent();

        // The dialog swaps the selected section in and out of its content host, so these fire each time
        // the user comes back to this section rather than once per dialog. They stay attached for the
        // lifetime of the control.
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // Runs each time the user returns to this section, so a throw here would otherwise escape an async
    // void handler and take the process with it.
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load the Workshop settings section");
            return;
        }

        // Wire auto-save only after the initial load has populated the fields, so
        // loading a stored connection does not trigger a save of its own values.
        if (_autoSaveTimer is null)
        {
            _autoSaveTimer = DispatcherQueue.CreateTimer();
            _autoSaveTimer.Interval = AutoSaveDelay;
            _autoSaveTimer.IsRepeating = false;
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;
        }

        WorkshopUrlTextBox.TextChanged += ConnectionField_Changed;
        AuthorTextBox.TextChanged += ConnectionField_Changed;

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        WorkshopUrlTextBox.TextChanged -= ConnectionField_Changed;
        AuthorTextBox.TextChanged -= ConnectionField_Changed;

        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;

        if (_autoSaveTimer is not null)
        {
            // A pending debounce means the user edited a field and is navigating away before the timer
            // fired. Flush the persist so the change is not lost.
            if (_autoSaveTimer.IsRunning)
            {
                ViewModel.SaveWorkshopConnection();
            }

            _autoSaveTimer.Stop();
            _autoSaveTimer.Tick -= AutoSaveTimer_Tick;
            _autoSaveTimer = null;
        }
    }

    // The password box is not bound, so the view clears and focuses it as the entry row appears. The
    // secret is never held anywhere the XAML can read it back.
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ViewModel.IsKeyEditVisible)
            || !ViewModel.IsKeyEditVisible)
        {
            return;
        }

        WorkshopKeyPasswordBox.Password = string.Empty;
        WorkshopKeyPasswordBox.Focus(FocusState.Programmatic);
    }

    private void WorkshopKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.KeyInput = WorkshopKeyPasswordBox.Password;
    }

    private void WorkshopKeyPasswordBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter
            && ViewModel.SaveWorkshopKeyCommand.CanExecute(null)
            && ViewModel.IsSaveKeyEnabled)
        {
            ViewModel.SaveWorkshopKeyCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            ViewModel.CancelChangeWorkshopKeyCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ConnectionField_Changed(object sender, TextChangedEventArgs e)
    {
        // A programmatic field update (the initial load) must not schedule a save of its own values.
        if (ViewModel.IsApplyingProgrammaticChange)
        {
            return;
        }

        RestartAutoSaveTimer();
    }

    private void RestartAutoSaveTimer()
    {
        _autoSaveTimer?.Stop();
        _autoSaveTimer?.Start();
    }

    private void AutoSaveTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        ViewModel.SaveWorkshopConnection();
    }
}
