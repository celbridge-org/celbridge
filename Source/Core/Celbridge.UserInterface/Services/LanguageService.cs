using System.Globalization;
using Celbridge.Localization;
using Celbridge.Logging;
using Celbridge.Settings;

namespace Celbridge.UserInterface.Services;

public class LanguageService : ILanguageService
{
    private readonly ISettingsService _settingsService;
    private readonly IMessengerService _messengerService;
    private readonly ILogger<LanguageService> _logger;

    // The language the operating system started us in, captured before anything overrides the culture, so
    // clearing the stored choice can return to it.
    private readonly string _systemLanguage;

    private string _currentLanguage;

    public LanguageService(
        ISettingsService settingsService,
        IMessengerService messengerService,
        ILogger<LanguageService> logger)
    {
        _settingsService = settingsService;
        _messengerService = messengerService;
        _logger = logger;

        _systemLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        _currentLanguage = _systemLanguage;
    }

    public string CurrentLanguage => _currentLanguage;

    public void ApplyStoredLanguage()
    {
        ApplyLanguage(_settingsService.Get(SettingCatalog.Application.Language));
    }

    public void SetLanguage(string language)
    {
        _settingsService.Set(SettingCatalog.Application.Language, language);

        ApplyLanguage(language);

        _logger.LogInformation("Application language set to {Language}", _currentLanguage);

        var message = new LanguageChangedMessage(_currentLanguage);
        _messengerService.Send(message);
    }

    // Resolves the stored choice to a language that actually exists, falling back to the operating system's,
    // and puts it in force. Resolved here rather than on every read of CurrentLanguage so a stored value that
    // no runtime knows is reported once, not on every lookup.
    private void ApplyLanguage(string language)
    {
        _currentLanguage = ResolveLanguage(language);

        var culture = CultureInfo.GetCultureInfo(_currentLanguage);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    private string ResolveLanguage(string language)
    {
        if (string.IsNullOrEmpty(language))
        {
            return _systemLanguage;
        }

        try
        {
            // The stored language came from a settings file that anything could have written.
            // predefinedOnly, because without it ICU invents a custom culture for any well formed name
            // rather than rejecting one the runtime has no strings for.
            CultureInfo.GetCultureInfo(language, predefinedOnly: true);
        }
        catch (CultureNotFoundException ex)
        {
            _logger.LogWarning(ex, "Ignored an unknown application language: {Language}", language);
            return _systemLanguage;
        }

        return language;
    }
}
