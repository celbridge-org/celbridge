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
/// The user's answer to the Download File dialog. Folder is the folder in the project that the In project option
/// saves to, and is empty for the project folder itself. FilePath is the absolute path an Other location answer
/// saves to.
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
