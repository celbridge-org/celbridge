namespace Celbridge.Explorer.Menu;

/// <summary>
/// Standard menu groups for the Explorer context menu.
/// </summary>
public static class ExplorerMenuGroups
{
    /// <summary>
    /// Group for document-related actions.
    /// </summary>
    public const string DocumentActions = nameof(DocumentActions);

    /// <summary>
    /// Group for creating new items.
    /// </summary>
    public const string AddItems = nameof(AddItems);

    /// <summary>
    /// Group for clipboard operations.
    /// </summary>
    public const string Clipboard = nameof(Clipboard);

    /// <summary>
    /// Group for utility operations.
    /// </summary>
    public const string Utilities = nameof(Utilities);

    /// <summary>
    /// Group for file system integration.
    /// </summary>
    public const string FileSystem = nameof(FileSystem);

    /// <summary>
    /// Group for custom extensions.
    /// </summary>
    public const string Extensions = nameof(Extensions);

    /// <summary>
    /// List of option groups, in display order.
    /// </summary>
    public static IReadOnlyList<string> OrderedGroups { get; } = new[]
    {
        DocumentActions,
        AddItems,
        Clipboard,
        Utilities,
        FileSystem,
        Extensions
    };
}

