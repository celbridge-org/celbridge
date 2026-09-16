namespace Celbridge.Dialog;

/// <summary>
/// Manages the display of file and folder pickers.
/// </summary>
public interface IFilePickerService
{
    /// <summary>
    /// Displays a file picker dialog and returns the path of the selected file.
    /// </summary>
    Task<Result<string>> PickSingleFileAsync(IEnumerable<string> fileExtensions);

    /// <summary>
    /// Displays a folder picker dialog and returns the path of the selected folder.
    /// </summary>
    Task<Result<string>> PickSingleFolderAsync();

    /// <summary>
    /// Displays a save file dialog suggesting a file name, and returns the path the user chose. The dialog
    /// offers a single file type, described by the description and matched by the extensions.
    /// </summary>
    Task<Result<string>> PickSaveFileAsync(string suggestedFileName, string fileTypeDescription, IEnumerable<string> fileExtensions);
}
