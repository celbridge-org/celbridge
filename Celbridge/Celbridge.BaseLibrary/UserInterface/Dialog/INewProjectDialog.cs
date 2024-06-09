namespace Celbridge.BaseLibrary.UserInterface.Dialog;

public interface INewProjectDialog
{
    /// <summary>
    /// Present the New Project dialog to the user.
    /// The async call completes when the user closes the dialog.
    /// Returns the path to the new project file if the project was successfully created.
    /// </summary>
    Task<Result<string>> ShowDialogAsync();
}
