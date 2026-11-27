namespace Celbridge.Dialog;

/// <summary>
/// Lists what the resource picker asks the user to choose.
/// </summary>
public enum ResourcePickerMode
{
    /// <summary>
    /// An existing file to open.
    /// </summary>
    Open,

    /// <summary>
    /// A place to save a file: a folder, and a name the user types. The name can name an existing file. The user then
    /// agrees to replace the file.
    /// </summary>
    Save,

    /// <summary>
    /// A folder in the project, the project folder included.
    /// </summary>
    Folder
}

/// <summary>
/// Sets up the resource picker: what the user chooses, the files the picker lists, where the picker starts and the
/// dialog's title.
/// </summary>
public record ResourcePickerOptions
{
    /// <summary>
    /// Returns the mode, which sets what the user chooses. Defaults to open mode.
    /// </summary>
    public ResourcePickerMode Mode { get; init; } = ResourcePickerMode.Open;

    /// <summary>
    /// The file extensions the picker lists, for example ".png". An empty list lists every file. In save mode, the
    /// name of the file to save must end in one of the extensions. Folder mode ignores the list.
    /// </summary>
    public IReadOnlyList<string> Extensions { get; init; } = [];

    /// <summary>
    /// Where the picker starts, or null for none. In open mode, a file to select in its folder. In save mode, a
    /// folder and a suggested name for the file to save. In folder mode, a folder to open. The picker ignores a
    /// resource outside the project, and a resource in a folder that is missing.
    /// </summary>
    public ResourceKey? DefaultResource { get; init; }

    /// <summary>
    /// The folder the picker opens on when the default resource is null or ignored. Empty for the project folder. A
    /// missing folder gives way to the project folder. In folder mode, the primary button chooses the open folder.
    /// </summary>
    public ResourceKey StartFolder { get; init; } = ResourceKey.Empty;

    /// <summary>
    /// The dialog's title, already localized. Null gives the mode's default title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// When true, the picker shows a preview of the selected image file. Applies to open mode only.
    /// </summary>
    public bool ShowPreview { get; init; }
}

/// <summary>
/// A modal dialog that asks the user to choose a file to open, a place to save a file, or a folder, in the project.
/// </summary>
public interface IResourcePickerDialog
{
    /// <summary>
    /// Present the Resource Picker Dialog to the user. Returns the chosen resource key, or fails if the dialog was
    /// cancelled. In save mode, the key names a file in an existing folder, and the file may not exist yet.
    /// </summary>
    Task<Result<ResourceKey>> ShowDialogAsync();
}
