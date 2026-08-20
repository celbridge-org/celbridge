using System.Globalization;
using Celbridge.Localization;
using Celbridge.Messaging;
using Celbridge.Settings;
using Celbridge.Tests.Helpers;
using Celbridge.UserInterface.Services;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the service that owns the application's language. The stored choice comes from a settings
/// file that anything could have written, so the service has to resolve it to a language that exists.
/// </summary>
[TestFixture]
public class LanguageServiceTests
{
    private ISettingsService _settingsService = null!;
    private IMessengerService _messengerService = null!;
    private RecordingLogger<LanguageService> _logger = null!;
    private LanguageService _languageService = null!;

    private string _storedLanguage = string.Empty;
    private string _systemLanguage = string.Empty;
    private CultureInfo _originalCulture = null!;

    [SetUp]
    public void SetUp()
    {
        // The service captures the culture it starts in, and applying a language mutates process-wide
        // culture state, so the original is restored in TearDown.
        _originalCulture = CultureInfo.CurrentUICulture;
        _systemLanguage = _originalCulture.TwoLetterISOLanguageName;

        _storedLanguage = string.Empty;
        _settingsService = Substitute.For<ISettingsService>();
        _settingsService
            .Get(SettingCatalog.Application.Language)
            .Returns(_ => _storedLanguage);
        _settingsService
            .When(settings => settings.Set(SettingCatalog.Application.Language, Arg.Any<string>()))
            .Do(call => _storedLanguage = call.ArgAt<string>(1));

        _messengerService = Substitute.For<IMessengerService>();
        _logger = new RecordingLogger<LanguageService>();
        _languageService = new LanguageService(_settingsService, _messengerService, _logger);
    }

    [TearDown]
    public void TearDown()
    {
        CultureInfo.CurrentUICulture = _originalCulture;
        CultureInfo.DefaultThreadCurrentUICulture = null;
    }

    [Test]
    public void ApplyStoredLanguage_WithNoStoredChoice_FollowsTheOperatingSystem()
    {
        _languageService.ApplyStoredLanguage();

        _languageService.CurrentLanguage.Should().Be(_systemLanguage);
    }

    [Test]
    public void SetLanguage_StoresAppliesAndAnnouncesIt()
    {
        _languageService.SetLanguage("fr");

        _languageService.CurrentLanguage.Should().Be("fr");
        _storedLanguage.Should().Be("fr");
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Should().Be("fr");

        // Hosted web surfaces re-localize off this message, so a language change that does not announce
        // itself leaves every open editor in the previous language.
        _messengerService.Received(1).Send(Arg.Is<LanguageChangedMessage>(message => message.Language == "fr"));
    }

    [Test]
    public void SetLanguage_WithEmpty_ReturnsToTheOperatingSystemLanguage()
    {
        _languageService.SetLanguage("fr");

        _languageService.SetLanguage(string.Empty);

        _languageService.CurrentLanguage.Should().Be(_systemLanguage);
        _messengerService.Received(1).Send(Arg.Is<LanguageChangedMessage>(message => message.Language == _systemLanguage));
    }

    [Test]
    public void ApplyStoredLanguage_FollowingTheOperatingSystem_KeepsItsCultureVerbatim()
    {
        // Narrowing en-US to en would break resource lookup outright: resources fall back from the specific
        // to the neutral, never the other way, and the only native resource file is en-US.
        var systemCulture = CultureInfo.CurrentUICulture;

        _languageService.ApplyStoredLanguage();

        CultureInfo.CurrentUICulture.Name.Should().Be(systemCulture.Name);
    }

    [Test]
    public void ApplyStoredLanguage_WithAnUnknownLanguage_FallsBackAndWarnsOnce()
    {
        _storedLanguage = "not-a-language";

        _languageService.ApplyStoredLanguage();

        // The fallback matters more than the warning: a bogus setting must not leave editors asking the host
        // for strings in a language nothing has.
        _languageService.CurrentLanguage.Should().Be(_systemLanguage);
        _logger.EntriesAt(LogEntryLevel.Warning).Should().HaveCount(1);

        // Reading the resolved language again must not re-report it.
        _ = _languageService.CurrentLanguage;
        _logger.EntriesAt(LogEntryLevel.Warning).Should().HaveCount(1);
    }
}
