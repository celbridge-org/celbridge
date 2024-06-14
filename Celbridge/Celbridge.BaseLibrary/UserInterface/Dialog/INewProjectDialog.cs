using Celbridge.BaseLibrary.Project;

namespace Celbridge.BaseLibrary.UserInterface.Dialog;

public interface INewProjectDialog
{
    /// <summary>
    /// Present the New Project dialog to the user.
    /// The async call completes when the user closes the dialog.
    /// Returns the config information specified by the the user to create the new project.
    /// </summary>
    Task<Result<NewProjectConfig>> ShowDialogAsync();

}
