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

    // The culture the operating system started us in, captured before anything overrides it, so clearing the
    // stored choice returns exactly what was there rather than an approximation of it.
    private readonly CultureInfo _systemCulture;

    // Its two-letter code, which is what a hosted page loads its strings by (localization/en.json).
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

        _systemCulture = CultureInfo.CurrentUICulture;
        _systemLanguage = _systemCulture.TwoLetterISOLanguageName;
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

        // Following the operating system restores its culture verbatim rather than the two-letter code:
        // resources fall back from the specific to the neutral and never the other way, so narrowing en-US
        // to en would stop an en-US resource file resolving at all.
        var culture = _currentLanguage == _systemLanguage
            ? _systemCulture
            : CultureInfo.GetCultureInfo(_currentLanguage);

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
