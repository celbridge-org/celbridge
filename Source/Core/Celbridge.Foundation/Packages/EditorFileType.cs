namespace Celbridge.Packages;

/// <summary>
/// A file type declared by an editor contribution.
/// Declares the file extension the editor handles and an optional display name or localization key
/// shown in the Add File dialog.
/// </summary>
public record EditorFileType
{
    /// <summary>
    /// The file extension this editor handles (e.g., ".note").
    /// </summary>
    public string FileExtension { get; init; } = string.Empty;

    /// <summary>
    /// Display name or localization key shown in the Add File dialog.
    /// When omitted, falls back to the extension name.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;
}
