using Celbridge.ContextMenu;
using Celbridge.Logging;

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

            // Create menu item
            var menuItem = new MenuFlyoutItem
            {
                Text = item.DisplayInfo.LocalizedText,
                IsEnabled = item.State.IsEnabled
            };

            // Add icon if specified
            if (!string.IsNullOrEmpty(item.DisplayInfo.IconGlyph))
            {
                menuItem.Icon = new FontIcon { Glyph = item.DisplayInfo.IconGlyph };
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


