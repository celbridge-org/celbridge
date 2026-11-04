namespace Celbridge.Dialog;

/// <summary>
/// Lists the ways the user can answer the Download File dialog.
/// </summary>
public enum DownloadFileChoice
{
    /// <summary>
    /// The user stopped the download.
    /// </summary>
    Cancel,

    /// <summary>
    /// The user chose to save the file to a folder in the project.
    /// </summary>
    ProjectFolder
}

/// <summary>
/// The user's answer to the Download File dialog. Folder is the project folder the file goes to, and is empty for
/// the project folder itself.
/// </summary>
public record DownloadFileAnswer(DownloadFileChoice Choice, ResourceKey Folder);

/// <summary>
/// A modal dialog that asks the user where to save a file downloaded in a web view.
/// </summary>
public interface IDownloadFileDialog
{
    /// <summary>
    /// Presents the dialog and returns the user's answer.
    /// </summary>
    Task<DownloadFileAnswer> ShowDialogAsync();
}
