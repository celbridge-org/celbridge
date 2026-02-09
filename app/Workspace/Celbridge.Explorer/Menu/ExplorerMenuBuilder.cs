using Celbridge.ContextMenu;
using Celbridge.Logging;
using Celbridge.UserInterface.ContextMenu;

namespace Celbridge.Explorer.Menu;

/// <summary>
/// Menu builder for the Explorer resource tree context menu.
/// </summary>
public class ExplorerMenuBuilder : MenuBuilder<ExplorerMenuContext>
{
    public ExplorerMenuBuilder(
        ILogger<MenuBuilder<ExplorerMenuContext>> logger,
        IEnumerable<IMenuOption<ExplorerMenuContext>> options)
        : base(logger, ExplorerMenuGroups.OrderedGroups, options)
    {
    }
}

