namespace Celbridge.Localization;

/// <summary>
/// Message sent when the application language changes, carrying the two-letter code of the new language.
/// </summary>
public record LanguageChangedMessage(string Language);

/// <summary>
/// The language the application presents itself in. Owns the stored choice and the effective language that
/// falls back to the operating system's, so no caller has to decide what the current language is.
/// </summary>
public interface ILanguageService
{
    /// <summary>
    /// The two-letter code of the language the application is currently presenting, e.g. "en". Never empty:
    /// with no stored choice this is the operating system's language.
    /// </summary>
    string CurrentLanguage { get; }

    /// <summary>
    /// Applies the stored language choice, without announcing a change. Called once during startup, before
    /// any string is looked up.
    /// </summary>
    void ApplyStoredLanguage();

    /// <summary>
    /// Sets the language the application presents and stores the choice, or an empty string to follow the
    /// operating system. Hosted web surfaces re-localize immediately; the native user interface resolves its
    /// strings when it is built, so it presents the new language from the next launch.
    /// </summary>
    void SetLanguage(string language);
}
