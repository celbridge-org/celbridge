using Celbridge.Logging;
using Celbridge.Projects;

namespace Celbridge.Workspace.Services;

/// <summary>
/// Helper class for building custom shortcut menu items in the navigation bar.
/// </summary>
public class ShortcutMenuBuilder
{
    private readonly ILogger<ShortcutMenuBuilder> _logger;

    private Dictionary<string, string> _tagsToScriptDictionary = new();
    private List<KeyValuePair<IList<object>, NavigationViewItem>> _shortcutMenuItems = new();
    private NavigationViewItemSeparator? _separator;

    public ShortcutMenuBuilder(ILogger<ShortcutMenuBuilder> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Tries to get the script associated with a shortcut tag.
    /// </summary>
    public bool TryGetScript(string tag, out string? script)
    {
        return _tagsToScriptDictionary.TryGetValue(tag, out script);
    }

    /// <summary>
    /// Builds shortcut menu items from the navigation bar section configuration.
    /// </summary>
    public void BuildShortcutMenuItems(NavigationBarSection.CustomCommandNode rootNode, IList<object> menuItems)
    {
        // Check if there are any shortcuts to add
        bool hasShortcuts = rootNode.Nodes.Count > 0 || rootNode.CustomCommands.Count > 0;
        
        if (hasShortcuts)
        {
            // Add a separator before the custom shortcuts
            _separator = new NavigationViewItemSeparator();
            menuItems.Add(_separator);
        }

        AddShortcutMenuItems(rootNode, menuItems);
    }

    private void AddShortcutMenuItems(NavigationBarSection.CustomCommandNode node, IList<object> menuItems)
    {
        Dictionary<string, NavigationViewItem> newNodes = new();
        Dictionary<string, string> pathToScriptDictionary = new();

        foreach (var (k, v) in node.Nodes)
        {
            var newItem = new NavigationViewItem()
            {
                Name = k,
                Content = k
            };

            menuItems.Add(newItem);
            string newPath = v.Path + (v.Path.Length > 0 ? "." : "") + k;
            newNodes.Add(newPath, newItem);
            _shortcutMenuItems.Add(new KeyValuePair<IList<object>, NavigationViewItem>(menuItems, newItem));
            AddShortcutMenuItems(v, newItem.MenuItems);
        }

        foreach (var command in node.CustomCommands)
        {
            if (pathToScriptDictionary.ContainsKey(command.Path!))
            {
                _logger.LogWarning($"Shortcut command '{command.Name}' at path '{command.Path}' collides with an existing command; command will not be added.");
                continue;
            }

            Symbol? icon = null;
            if (command.Icon is not null)
            {
                if (Enum.TryParse(command.Icon, out Symbol parsedIcon))
                {
                    icon = parsedIcon;
                }
            }

            if (newNodes.ContainsKey(command.Path!))
            {
                NavigationViewItem item = newNodes[command.Path!];
                if (!string.IsNullOrEmpty(command.ToolTip))
                {
                    ToolTipService.SetToolTip(item, command.ToolTip);
                    ToolTipService.SetPlacement(item, PlacementMode.Right);
                }

                if (icon.HasValue)
                {
                    item.Icon = new SymbolIcon(icon.Value);
                }

                if (!string.IsNullOrEmpty(command.Name))
                {
                    item.Content = command.Name;
                }

                _logger.LogWarning($"Shortcut command '{command.Name}' at path '{command.Path}' collides with a folder node.");
                continue;
            }

            _tagsToScriptDictionary.Add(command.Path!, command.Script!);

            var commandItem = new NavigationViewItem
            {
                Name = command.Name ?? "Shortcut",
                Content = command.Name ?? "Shortcut",
                Tag = command.Path!
            };

            if (!string.IsNullOrEmpty(command.ToolTip))
            {
                ToolTipService.SetToolTip(commandItem, command.ToolTip);
                ToolTipService.SetPlacement(commandItem, PlacementMode.Right);
            }

            if (icon.HasValue)
            {
                commandItem.Icon = new SymbolIcon(icon.Value);
            }

            menuItems.Add(commandItem);
            _shortcutMenuItems.Add(new KeyValuePair<IList<object>, NavigationViewItem>(menuItems, commandItem));
        }
    }
}
