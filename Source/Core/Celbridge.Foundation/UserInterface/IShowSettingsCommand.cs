using Celbridge.Commands;

namespace Celbridge.UserInterface;

/// <summary>
/// Show the Application Settings.
/// </summary>
public interface IShowSettingsCommand : IExecutableCommand
{
    /// <summary>
    /// The category to open on, from SettingsDialogSections. Empty opens the category the user last had
    /// open. Opening on a category does not change which one that is.
    /// </summary>
    string SectionKey { get; set; }
}
