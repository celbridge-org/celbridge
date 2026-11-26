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
    /// The file extensions the picker lists, for example ".png". An empty list lists every file. Folder mode ignores
    /// the list.
    /// </summary>
    public IReadOnlyList<string> Extensions { get; init; } = [];

    /// <summary>
    /// A file or folder to select when the picker opens. The picker opens on the resource's folder. Empty for none.
    /// Applies to open mode only.
    /// </summary>
    public ResourceKey InitialSelection { get; init; } = ResourceKey.Empty;

    /// <summary>
    /// The folder the picker opens on when there is no initial selection, or the initial selection's folder is
    /// missing. Empty for the project folder. In folder mode, the primary button returns this folder until the user
    /// opens another.
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
/// A modal dialog that allows the user to pick a resource from the project.
/// </summary>
public interface IResourcePickerDialog
{
    /// <summary>
    /// Present the Resource Picker Dialog to the user.
    /// Returns the selected resource key on success, or fails if the dialog was cancelled.
    /// </summary>
    Task<Result<ResourceKey>> ShowDialogAsync();
}
