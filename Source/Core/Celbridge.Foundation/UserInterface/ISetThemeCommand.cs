using Celbridge.Commands;
using Celbridge.Settings;

namespace Celbridge.UserInterface;

/// <summary>
/// A command that selects the application colour theme.
/// </summary>
public interface ISetThemeCommand : IExecutableCommand
{
    /// <summary>
    /// The colour theme to apply.
    /// </summary>
    ApplicationColorTheme Theme { get; set; }
}
