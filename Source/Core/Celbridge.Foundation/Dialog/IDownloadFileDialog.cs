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
    InProject,

    /// <summary>
    /// The user chose a path in the system save dialog. The path can be inside the project or outside it.
    /// </summary>
    OtherLocation
}

/// <summary>
/// The user's answer to the Download File dialog. An In Project answer saves to Folder, a folder in the project.
/// Folder is empty for the project folder itself. An Other Location answer saves to FilePath, an absolute path.
/// </summary>
public record DownloadFileAnswer(DownloadFileChoice Choice, ResourceKey Folder, string FilePath = "");

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
