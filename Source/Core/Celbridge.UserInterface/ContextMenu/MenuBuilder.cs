using Celbridge.ContextMenu;
using Celbridge.Logging;
using Celbridge.UserInterface.Views.Controls;

namespace Celbridge.UserInterface.ContextMenu;

/// <summary>
/// Builds a context menu that organizes menu options by priority and group.
/// </summary>
public class MenuBuilder<TContext> : IMenuBuilder<TContext> where TContext : IMenuContext
{
    private readonly ILogger<MenuBuilder<TContext>> _logger;
    private readonly IReadOnlyList<string> _orderedGroups;
    private readonly IEnumerable<IMenuOption<TContext>> _options;

    public MenuBuilder(
        ILogger<MenuBuilder<TContext>> logger,
        IReadOnlyList<string> orderedGroups,
        IEnumerable<IMenuOption<TContext>> options)
    {
        _logger = logger;
        _orderedGroups = orderedGroups;
        _options = options;
    }

    public IList<MenuFlyoutItemBase> BuildMenuItems(TContext context)
    {
        var items = new List<MenuFlyoutItemBase>();

        var visibleOptions = _options
            .Select(option => new
            {
                Option = option,
                State = option.GetState(context),
                DisplayInfo = option.GetDisplayInfo(context)
            })
            .Where(x => x.State.IsVisible)  // Filter out non-visible items.
            .OrderBy(x => GetGroupOrder(x.Option.GroupId))  // Group order first
            .ThenBy(x => x.Option.Priority)                 // Priority within group
            .ToList();

        if (visibleOptions.Count == 0)
        {
            return items;
        }

        // Group by GroupId and add separators between groups
        string? lastGroupId = null;
        foreach (var item in visibleOptions)
        {
            // Add separator if we're starting a new group (and it's not the first group)
            if (lastGroupId != null && item.Option.GroupId != lastGroupId)
            {
                items.Add(new MenuFlyoutSeparator());
            }

            // An option that yields more than one dynamic child renders as a submenu. Otherwise it is a
            // flat item wired to the option's own Execute (which handles the single-child case).
            var subMenuItems = (item.Option as ISubMenuOption<TContext>)?.GetSubMenuItems(context);
            if (subMenuItems is not null && subMenuItems.Count > 1)
            {
                var subItem = new MenuFlyoutSubItem
                {
                    Text = item.DisplayInfo.LocalizedText,
                    IsEnabled = item.State.IsEnabled
                };

                if (item.DisplayInfo.Icon is IconSymbol subItemIcon)
                {
                    subItem.Icon = new Icon { Symbol = subItemIcon };
                }

                foreach (var child in subMenuItems)
                {
                    var childItem = new MenuFlyoutItem { Text = child.LocalizedText };
                    if (child.Icon is IconSymbol childIcon)
                    {
                        childItem.Icon = new Icon { Symbol = childIcon };
                    }

                    var childExecute = child.Execute; // Capture for closure
                    childItem.Click += (_, _) =>
                    {
                        _logger.LogDebug("Context menu submenu item selected: {OptionType}", item.Option.GetType().Name);
                        childExecute();
                    };
                    subItem.Items.Add(childItem);
                }

                items.Add(subItem);
                lastGroupId = item.Option.GroupId;
                continue;
            }

            // Create menu item
            var menuItem = new MenuFlyoutItem
            {
                Text = item.DisplayInfo.LocalizedText,
                IsEnabled = item.State.IsEnabled
            };

            // Add icon if specified
            if (item.DisplayInfo.Icon is IconSymbol iconSymbol)
            {
                menuItem.Icon = new Icon { Symbol = iconSymbol };
            }

            // A display-only hint. The chord is handled by the focused surface, not by this menu item.
            if (item.DisplayInfo.ShortcutHint is string shortcutHint)
            {
                menuItem.KeyboardAcceleratorTextOverride = shortcutHint;
            }

            // Wire up click handler
            var option = item.Option; // Capture for closure
            menuItem.Click += (_, _) =>
            {
                _logger.LogDebug("Context menu option selected: {OptionType}", option.GetType().Name);
                option.Execute(context);
            };

            items.Add(menuItem);
            lastGroupId = item.Option.GroupId;
        }

        return items;
    }

    private int GetGroupOrder(string groupId)
    {
        for (int i = 0; i < _orderedGroups.Count; i++)
        {
            if (_orderedGroups[i] == groupId)
                return i;
        }
        return int.MaxValue;
    }
}


