namespace Celbridge.ContextMenu;

/// <summary>
/// Display information for a menu option.
/// </summary>
public record MenuItemDisplayInfo(string LocalizedText, string? IconGlyph = null);

/// <summary>
/// State information for a menu option.
/// </summary>
public record MenuItemState(bool IsVisible, bool IsEnabled);

/// <summary>
/// Represents a group of related menu options.
/// </summary>
public partial record MenuOptionGroup(string Id);

/// <summary>
/// Represents a single context menu option.
/// </summary>
public interface IMenuOption<TContext> where TContext : IMenuContext
{
    /// <summary>
    /// Group identifier for grouping related options in the context menu.
    /// </summary>
    string GroupId { get; }

    /// <summary>
    /// Priority for ordering menu options within a group.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Gets the display information for this menu option, based on the current context.
    /// </summary>
    MenuItemDisplayInfo GetDisplayInfo(TContext context);

    /// <summary>
    /// Gets the visibility and enabled state for this menu option, based on the current context.
    /// </summary>
    MenuItemState GetState(TContext context);

    /// <summary>
    /// Executes the menu option's action.
    /// </summary>
    void Execute(TContext context);
}
