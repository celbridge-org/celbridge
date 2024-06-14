namespace Celbridge.BaseLibrary.FilePicker;

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
}
