using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Settings;

namespace Celbridge.UserInterface.ViewModels.Dialogs;

/// <summary>
/// A selectable application colour theme paired with its localized display name, for the theme combo box.
/// </summary>
public sealed class ThemeOption
{
    public ThemeOption(ApplicationColorTheme theme, string displayName)
    {
        Theme = theme;
        DisplayName = displayName;
    }

    public ApplicationColorTheme Theme { get; }

    public string DisplayName { get; }
}

public partial class SettingsDialogViewModel : ObservableObject
{
    private readonly ILogger<SettingsDialogViewModel> _logger;
    private readonly ISettingsService _settingsService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    [ObservableProperty]
    private ThemeOption? _selectedTheme;

    private bool _isReflectingStoredTheme;

    public SettingsDialogViewModel(
        ILogger<SettingsDialogViewModel> logger,
        ISettingsService settingsService,
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IMessengerService messengerService)
    {
        _logger = logger;
        _settingsService = settingsService;
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _messengerService = messengerService;

        ThemeOptions = BuildThemeOptions();

        ReflectStoredTheme();
    }

    public void OnOpened()
    {
        // The View menu can change the theme while this dialog is open, so follow it rather than showing
        // the value read at construction.
        _messengerService.Register<ThemeChangedMessage>(this, OnThemeChanged);

        ReflectStoredTheme();
    }

    public void OnClosed()
    {
        _messengerService.UnregisterAll(this);
    }

    private void OnThemeChanged(object recipient, ThemeChangedMessage message)
    {
        // The message carries the resolved light or dark theme, which cannot distinguish System from a
        // fixed theme that happens to match it. The stored setting can, so read that instead.
        ReflectStoredTheme();
    }

    private void ReflectStoredTheme()
    {
        var storedTheme = _settingsService.Get(SettingCatalog.Application.Theme);
        var storedOption = ThemeOptions.FirstOrDefault(themeOption => themeOption.Theme == storedTheme);

        // Reflecting the stored theme in the combo box must not run the changed handler, which would
        // dispatch a command to re-apply the theme that was just applied.
        _isReflectingStoredTheme = true;
        try
        {
            SelectedTheme = storedOption;
        }
        finally
        {
            _isReflectingStoredTheme = false;
        }
    }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value is null
            || _isReflectingStoredTheme)
        {
            return;
        }

        _ = ApplyThemeAsync(value.Theme);
    }

    // This dialog holds the command queue while it is open, so the theme is applied off the queue.
    // Enqueuing it would leave the choice unapplied until the dialog closed.
    private async Task ApplyThemeAsync(ApplicationColorTheme theme)
    {
        var setThemeResult = await _commandService.ExecuteImmediate<ISetThemeCommand>(command =>
        {
            command.Theme = theme;
        });

        if (setThemeResult.IsFailure)
        {
            _logger.LogError(setThemeResult, "Failed to set the application theme");
        }
    }

    private List<ThemeOption> BuildThemeOptions()
    {
        var themeOptions = new List<ThemeOption>();

        var themeValues = Enum.GetValues(typeof(ApplicationColorTheme));
        foreach (ApplicationColorTheme theme in themeValues)
        {
            var stringKey = "Theme_" + Enum.GetName(typeof(ApplicationColorTheme), theme);
            var displayName = _stringLocalizer.GetString(stringKey);
            if (displayName is null)
            {
                throw new NotImplementedException("Cannot find localised string entry for '" + stringKey + "'");
            }

            var themeOption = new ThemeOption(theme, displayName);
            themeOptions.Add(themeOption);
        }

        return themeOptions;
    }
}
