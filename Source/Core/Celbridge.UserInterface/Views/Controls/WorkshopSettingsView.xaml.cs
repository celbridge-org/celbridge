using Celbridge.UserInterface.ViewModels.Controls;
using Microsoft.UI.Dispatching;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// The Workshop connection section of the Settings page: URL and Author fields,
/// the Workshop Key entry, and a status bar. Composed onto SettingsPage.
/// </summary>
public sealed partial class WorkshopSettingsView : UserControl
{
    private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly IStringLocalizer _stringLocalizer;

    private DispatcherQueueTimer? _autoSaveTimer;
    private bool _connectionFieldDirty;

    private string WorkshopSectionString => _stringLocalizer.GetString("Settings_Workshop_SectionHeader");
    private string WorkshopDescriptionString => _stringLocalizer.GetString("Settings_Workshop_Description");
    private string WorkshopUrlString => _stringLocalizer.GetString("Settings_Workshop_Url");
    private string WorkshopUrlTooltipString => _stringLocalizer.GetString("Settings_Workshop_UrlTooltip");
    private string WorkshopKeyString => _stringLocalizer.GetString("Settings_Workshop_Key");
    private string AuthorString => _stringLocalizer.GetString("Settings_Workshop_Author");
    private string AuthorTooltipString => _stringLocalizer.GetString("Settings_Workshop_AuthorTooltip");
    private string AuthorPlaceholderString => _stringLocalizer.GetString("Settings_Workshop_AuthorPlaceholder");
    private string SetWorkshopKeyString => _stringLocalizer.GetString("Settings_Workshop_KeySet");
    private string ChangeKeyString => _stringLocalizer.GetString("Settings_Workshop_KeyChange");
    private string RemoveKeyString => _stringLocalizer.GetString("Settings_Workshop_KeyRemove");

    public WorkshopSettingsViewModel ViewModel { get; }

    public WorkshopSettingsView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<WorkshopSettingsViewModel>();

        this.InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();

        // Wire auto-save only after the initial load has populated the fields, so
        // loading a stored connection does not trigger a save of its own values.
        if (_autoSaveTimer is null)
        {
            _autoSaveTimer = DispatcherQueue.CreateTimer();
            _autoSaveTimer.Interval = AutoSaveDelay;
            _autoSaveTimer.IsRepeating = false;
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;
        }

        WorkshopUrlTextBox.TextChanged += WorkshopUrlField_Changed;
        AuthorTextBox.TextChanged += AuthorField_Changed;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;

        WorkshopUrlTextBox.TextChanged -= WorkshopUrlField_Changed;
        AuthorTextBox.TextChanged -= AuthorField_Changed;

        if (_autoSaveTimer is not null)
        {
            // A pending debounce means the user edited a field and is navigating
            // away before the timer fired. Flush it so the change is not lost; the
            // connection check is skipped because the section is going away.
            if (_autoSaveTimer.IsRunning)
            {
                _ = ViewModel.SaveWorkshopConnectionAsync(checkConnection: false);
            }

            _autoSaveTimer.Stop();
            _autoSaveTimer.Tick -= AutoSaveTimer_Tick;
            _autoSaveTimer = null;
        }
    }

    private void WorkshopUrlField_Changed(object sender, TextChangedEventArgs e)
    {
        OnConnectionFieldEdited();
    }

    private void AuthorField_Changed(object sender, TextChangedEventArgs e)
    {
        // The author does not affect connectivity, so an author-only edit saves
        // without re-verifying the connection.
        if (ViewModel.IsApplyingProgrammaticChange)
        {
            return;
        }

        RestartAutoSaveTimer();
    }

    // A URL or key edit marks the next auto-save as connection-affecting, so the
    // saved connection is verified against the workshop once it persists.
    private void OnConnectionFieldEdited()
    {
        if (ViewModel.IsApplyingProgrammaticChange)
        {
            return;
        }

        _connectionFieldDirty = true;
        RestartAutoSaveTimer();
    }

    private void RestartAutoSaveTimer()
    {
        _autoSaveTimer?.Stop();
        _autoSaveTimer?.Start();
    }

    private async void AutoSaveTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();

        var checkConnection = _connectionFieldDirty;
        _connectionFieldDirty = false;
        await ViewModel.SaveWorkshopConnectionAsync(checkConnection);
    }
}
