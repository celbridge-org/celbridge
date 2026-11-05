namespace Celbridge.Dialog;

/// <summary>
/// The folder a picker opens on.
/// </summary>
public enum PickerStartFolder
{
    /// <summary>
    /// The user's Documents folder.
    /// </summary>
    Documents,

    /// <summary>
    /// The user's Downloads folder.
    /// </summary>
    Downloads,

    /// <summary>
    /// The folder the picker last used under its settings identifier. The platform picks a folder the first time.
    /// </summary>
    LastUsed
}

/// <summary>
/// Manages the display of file and folder pickers.
/// </summary>
public interface IFilePickerService
{
    /// <summary>
    /// Displays a file picker dialog and returns the path of the selected file. The picker opens on the start
    /// folder. A settings identifier keeps the picker's last folder apart from the other pickers.
    /// </summary>
    Task<Result<string>> PickSingleFileAsync(
        IEnumerable<string> fileExtensions,
        PickerStartFolder startFolder = PickerStartFolder.Documents,
        string settingsIdentifier = "");

    /// <summary>
    /// Displays a folder picker dialog and returns the path of the selected folder. The picker opens on the start
    /// folder. A settings identifier keeps the picker's last folder apart from the other pickers.
    /// </summary>
    Task<Result<string>> PickSingleFolderAsync(
        PickerStartFolder startFolder = PickerStartFolder.Documents,
        string settingsIdentifier = "");

    /// <summary>
    /// Displays a save file dialog suggesting a file name, and returns the path the user chose. The dialog
    /// offers a single file type, described by the description and matched by the extensions. With an empty
    /// extension list, the dialog accepts any file type. The dialog opens on the start folder. A settings
    /// identifier keeps the dialog's last folder apart from the other pickers.
    /// </summary>
    Task<Result<string>> PickSaveFileAsync(
        string suggestedFileName,
        string fileTypeDescription,
        IEnumerable<string> fileExtensions,
        PickerStartFolder startFolder = PickerStartFolder.Documents,
        string settingsIdentifier = "");
}
