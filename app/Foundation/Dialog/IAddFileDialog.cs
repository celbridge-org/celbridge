using Celbridge.Explorer;

namespace Celbridge.Dialog;

/// <summary>
/// Configuration returned by the add file dialog.
/// </summary>
public record AddFileConfig(string FileName, ResourceFormat FileType);

/// <summary>
/// A modal dialog that allows the user to create a new file with a selected file type.
/// </summary>
public interface IAddFileDialog
{
    /// <summary>
    /// Present the Add File Dialog to the user.
    /// The async call completes when the user closes the dialog.
    /// Returns the filename and file type the user selected.
    /// </summary>
    Task<Result<AddFileConfig>> ShowDialogAsync();
}
