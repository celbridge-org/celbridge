namespace Celbridge.UserInterface;

/// <summary>
/// Describes a utility launcher button shown in the centre of the title bar. Built from a utility
/// document contribution; clicking the button opens or activates that utility document.
/// </summary>
public record UtilityButton
{
    /// <summary>
    /// Fully-qualified id of the utility to open, in "{packageName}.{documentId}" form.
    /// </summary>
    public required string UtilityId { get; init; }

    /// <summary>
    /// Bootstrap Icons glyph name for the button, sourced from the manifest icon field.
    /// </summary>
    public required string Icon { get; init; }

    /// <summary>
    /// Resolved, localized tooltip text. Drives both the hover tooltip and the accessible name.
    /// </summary>
    public required string Tooltip { get; init; }
}
