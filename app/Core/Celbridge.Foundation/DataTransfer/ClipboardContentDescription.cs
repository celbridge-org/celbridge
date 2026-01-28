namespace Celbridge.DataTransfer;

/// <summary>
/// The types of content that can be on the clipboard.
/// </summary>
public enum ClipboardContentType
{
    /// <summary>
    /// No content is on the clipboard.
    /// </summary>
    None,

    /// <summary>
    /// A resource is on the clipboard, such as an image or file.
    /// </summary>
    Resource,

    /// <summary>
    /// Text content is on the clipboard.
    /// </summary>
    Text,
}

/// <summary>
/// The operations that can be performed on clipboard content.
/// </summary>
public enum ClipboardContentOperation
{
    /// <summary>
    /// No operation is specified.
    /// </summary>
    None,

    /// <summary>
    /// The content should be copied.
    /// </summary>
    Copy,

    /// <summary>
    /// The content should be moved.
    /// </summary>
    Move,

    /// <summary>
    /// The content should be linked.
    /// </summary>
    Link
}

/// <summary>
/// Describes the content currently on the clipboard.
/// </summary>
public record ClipboardContentDescription(
    ClipboardContentType ContentType,
    ClipboardContentOperation ContentOperation
);
