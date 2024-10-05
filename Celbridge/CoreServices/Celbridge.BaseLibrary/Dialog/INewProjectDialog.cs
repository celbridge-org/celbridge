using Celbridge.Projects;

namespace Celbridge.Dialog;

/// <summary>
/// A modal dialog that allows the user to specify the configuration for a new project.
/// </summary>
public interface INewProjectDialog
{
    /// <summary>
    /// Present the New Project dialog to the user.
    /// Returns the new project config information specified by the the user in the dialog.
    /// Note that this method only constucts the new project config, it does not actually create the new project.
    /// The async call completes when the user closes the dialog.
    /// </summary>
    Task<Result<NewProjectConfig>> ShowDialogAsync();

}
