using Celbridge.Commands;

namespace Celbridge.Localization;

/// <summary>
/// Sets the language the application presents itself in.
/// </summary>
public interface ISetLanguageCommand : IExecutableCommand
{
    /// <summary>
    /// The two-letter code of the language to present, e.g. "fr", or an empty string to follow the
    /// operating system.
    /// </summary>
    string Language { get; set; }
}
