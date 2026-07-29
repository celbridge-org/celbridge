using Celbridge.UserInterface;

namespace Celbridge.ContextMenu;

/// <summary>
/// Display information for a menu option.
/// </summary>
public record MenuItemDisplayInfo(string LocalizedText, IconSymbol? Icon = null);

/// <summary>
/// State information for a menu option.
/// </summary>
public record MenuItemState(bool IsVisible, bool IsEnabled);

/// <summary>
/// Represents a group of related menu options.
/// </summary>
public partial record MenuOptionGroup(string Id);

/// <summary>
/// A dynamically generated child of a menu option: its label, optional icon, and the action to run when
/// chosen.
/// </summary>
public record SubMenuItem(string LocalizedText, IconSymbol? Icon, Action Execute);

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

/// <summary>
/// An option that can expand into a submenu. When it returns more than one child the builder renders a
/// submenu of those children. With one or none it renders a flat item wired to the option's own Execute.
/// </summary>
public interface ISubMenuOption<TContext> where TContext : IMenuContext
{
    /// <summary>
    /// Returns the dynamic children for this option in the current context, or an empty list to render as
    /// a single flat item.
    /// </summary>
    IReadOnlyList<SubMenuItem> GetSubMenuItems(TContext context);
}
