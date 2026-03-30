namespace Celbridge.Explorer.Menu;

/// <summary>
/// Standard menu groups for the Explorer context menu.
/// </summary>
public enum ExplorerMenuGroup
{
    DocumentActions,
    AddItems,
    EditActions,
    Utilities,
    FileSystem,
    Extensions
}

/// <summary>
/// Provides the display order for Explorer menu groups.
/// </summary>
public static class ExplorerMenuGroups
{
    /// <summary>
    /// List of option groups, in display order.
    /// </summary>
    public static IReadOnlyList<string> OrderedGroups { get; } = Enum.GetValues<ExplorerMenuGroup>()
        .Select(group => group.ToString())
        .ToList();
}
