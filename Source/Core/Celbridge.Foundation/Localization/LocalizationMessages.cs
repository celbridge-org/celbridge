namespace Celbridge.Localization;

/// <summary>
/// Message sent when the application language changes, carrying the two-letter code of the new language.
/// </summary>
public record LanguageChangedMessage(string Language);
