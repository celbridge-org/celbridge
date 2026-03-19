using Celbridge.ContextMenu;

namespace Celbridge.UserInterface.ContextMenu;

/// <summary>
/// Service for building context menus from menu options based on the interaction context.
/// </summary>
public interface IMenuBuilder<TContext> where TContext : IMenuContext
{
    /// <summary>
    /// Builds a list of menu flyout items based on the current context.
    /// </summary>
    IList<MenuFlyoutItemBase> BuildMenuItems(TContext context);
}

