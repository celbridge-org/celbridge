namespace Celbridge.Extensions;

/// <summary>
/// A document file type declared by an extension.
/// Declares the file extension the editor handles and an optional display name or localization key
/// shown in the Add File dialog.
/// </summary>
public record DocumentFileType
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
