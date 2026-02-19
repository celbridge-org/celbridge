namespace Celbridge.UserInterface;

/// <summary>
/// Describes a shortcut button to display in the title bar.
/// The name property contains the full hierarchical path using "/" as separator.
/// Example: "Run Examples/Hello World" creates a "Hello World" item under "Run Examples" group.
/// </summary>
public record Shortcut
{
    private const char PathSeparator = '/';

    /// <summary>
    /// Full hierarchical name of the shortcut (required).
    /// Use "/" to create nested items, e.g., "Tools/Format Code".
    /// The display text is the last segment of the path.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Icon name from symbol registry.
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Hover text; defaults to DisplayName if not specified.
    /// </summary>
    public string? Tooltip { get; init; }

    /// <summary>
    /// Script to execute. Required for leaf items, omit for groups.
    /// </summary>
    public string? Script { get; init; }

    /// <summary>
    /// Returns the display text (last segment of the name path).
    /// Example: "Run Examples/Hello World" returns "Hello World".
    /// </summary>
    public string DisplayName
    {
        get
        {
            var lastSeparator = Name.LastIndexOf(PathSeparator);
            return lastSeparator >= 0 ? Name[(lastSeparator + 1)..] : Name;
        }
    }

    /// <summary>
    /// Returns the parent path (everything before the last segment), or null if top-level.
    /// Example: "Run Examples/Hello World" returns "Run Examples".
    /// </summary>
    public string? ParentPath
    {
        get
        {
            var lastSeparator = Name.LastIndexOf(PathSeparator);
            return lastSeparator >= 0 ? Name[..lastSeparator] : null;
        }
    }

    /// <summary>
    /// Returns true if this shortcut is a group container (no script defined).
    /// </summary>
    public bool IsGroup => string.IsNullOrEmpty(Script);

    /// <summary>
    /// Returns true if this is a top-level shortcut (no "/" in name).
    /// </summary>
    public bool IsTopLevel => ParentPath == null;
}
