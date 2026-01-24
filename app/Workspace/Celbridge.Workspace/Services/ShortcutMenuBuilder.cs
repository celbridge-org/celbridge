using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Workspace.Views.Controls;

namespace Celbridge.Workspace.Services;

/// <summary>
/// Helper class for building custom shortcut buttons in the navigation bar.
/// </summary>
public class ShortcutMenuBuilder
{
    private const char PathSeparator = '/';

    private readonly ILogger<ShortcutMenuBuilder> _logger;

    private Dictionary<string, string> _tagsToScriptDictionary = new();

    /// <summary>
    /// Event raised when a shortcut button is clicked.
    /// </summary>
    public event Action<string>? ShortcutClicked;

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
    /// Builds shortcut buttons from the shortcuts section configuration.
    /// </summary>
    public bool BuildShortcutButtons(ShortcutsSection shortcutsSection, StackPanel panel)
    {
        var definitions = shortcutsSection.Definitions;
        
        if (definitions.Count == 0)
        {
            return false;
        }

        // Build tree structure from flat list
        var rootNode = BuildTreeFromDefinitions(definitions);
        
        // Add buttons to panel
        AddShortcutButtonsFromTree(rootNode, panel);
        
        return true;
    }

    /// <summary>
    /// Internal tree node for building the UI hierarchy.
    /// </summary>
    private class ShortcutTreeNode
    {
        public ShortcutDefinition? Definition { get; set; }
        public Dictionary<string, ShortcutTreeNode> Children { get; } = new();
        public List<ShortcutDefinition> LeafItems { get; } = new();
    }

    /// <summary>
    /// Build a tree structure from the flat list of shortcut definitions.
    /// </summary>
    private ShortcutTreeNode BuildTreeFromDefinitions(IReadOnlyList<ShortcutDefinition> definitions)
    {
        var root = new ShortcutTreeNode();
        
        // First pass: create group nodes
        foreach (var def in definitions)
        {
            if (def.IsGroup)
            {
                // Find or create the parent path, then add this group
                var parentNode = GetOrCreateNodeAtPath(root, def.ParentPath);
                
                // Create or update the node for this group
                if (!parentNode.Children.TryGetValue(def.DisplayName, out var groupNode))
                {
                    groupNode = new ShortcutTreeNode();
                    parentNode.Children[def.DisplayName] = groupNode;
                }
                groupNode.Definition = def;
            }
        }

        // Second pass: add leaf items (non-groups)
        foreach (var def in definitions)
        {
            if (!def.IsGroup)
            {
                if (def.IsTopLevel)
                {
                    // Top-level command - add directly to root's leaf items
                    root.LeafItems.Add(def);
                }
                else
                {
                    // Nested command - find the parent node and add to its leaf items
                    var parentNode = GetNodeAtPath(root, def.ParentPath!);
                    if (parentNode != null)
                    {
                        parentNode.LeafItems.Add(def);
                    }
                    else
                    {
                        _logger.LogWarning($"Parent path '{def.ParentPath}' not found for shortcut '{def.Name}'");
                    }
                }
            }
        }

        return root;
    }

    /// <summary>
    /// Get or create a node at the specified path.
    /// </summary>
    private ShortcutTreeNode GetOrCreateNodeAtPath(ShortcutTreeNode root, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return root;
        }

        var segments = path.Split(PathSeparator);
        var current = root;

        foreach (var segment in segments)
        {
            if (!current.Children.TryGetValue(segment, out var child))
            {
                child = new ShortcutTreeNode();
                current.Children[segment] = child;
            }
            current = child;
        }

        return current;
    }

    /// <summary>
    /// Get a node at the specified path, or null if not found.
    /// </summary>
    private ShortcutTreeNode? GetNodeAtPath(ShortcutTreeNode root, string path)
    {
        var segments = path.Split(PathSeparator);
        var current = root;

        foreach (var segment in segments)
        {
            if (!current.Children.TryGetValue(segment, out var child))
            {
                return null;
            }
            current = child;
        }

        return current;
    }

    /// <summary>
    /// Add shortcut buttons from the tree structure.
    /// </summary>
    private void AddShortcutButtonsFromTree(ShortcutTreeNode node, StackPanel panel)
    {
        // Add group nodes as buttons with flyout menus
        foreach (var (name, childNode) in node.Children)
        {
            var def = childNode.Definition;
            
            var button = CreateShortcutButton(
                def?.DisplayName ?? name, 
                def?.Icon, 
                def?.Tooltip);
            
            // Create a flyout menu for the child items
            var flyout = new MenuFlyout();
            flyout.Placement = FlyoutPlacementMode.RightEdgeAlignedTop;
            AddMenuItemsFromTree(childNode, flyout.Items);
            button.SetFlyout(flyout);
            
            panel.Children.Add(button);
        }

        // Add leaf items as buttons
        foreach (var def in node.LeafItems)
        {
            var tag = def.Name; // Use full name as tag for uniqueness
            
            if (_tagsToScriptDictionary.ContainsKey(tag))
            {
                _logger.LogWarning($"Shortcut '{def.Name}' collides with an existing command; command will not be added.");
                continue;
            }

            _tagsToScriptDictionary.Add(tag, def.Script!);

            var button = CreateShortcutButton(def.DisplayName, def.Icon, def.Tooltip);
            button.Tag = tag;
            button.Click += OnShortcutButtonClick;
            
            panel.Children.Add(button);
        }
    }

    /// <summary>
    /// Add menu items from the tree structure recursively.
    /// </summary>
    private void AddMenuItemsFromTree(ShortcutTreeNode node, IList<MenuFlyoutItemBase> menuItems)
    {
        // Add sub-groups as sub-menus
        foreach (var (name, childNode) in node.Children)
        {
            var def = childNode.Definition;
            
            var subMenu = new MenuFlyoutSubItem
            {
                Text = def?.DisplayName ?? name
            };
            
            if (!string.IsNullOrEmpty(def?.Icon) && 
                Enum.TryParse<Symbol>(def.Icon, out var icon))
            {
                subMenu.Icon = new SymbolIcon(icon);
            }
            
            AddMenuItemsFromTree(childNode, subMenu.Items);
            menuItems.Add(subMenu);
        }

        // Add leaf items as menu items
        foreach (var def in node.LeafItems)
        {
            var tag = def.Name; // Use full name as tag for uniqueness
            
            if (_tagsToScriptDictionary.ContainsKey(tag))
            {
                _logger.LogWarning($"Shortcut '{def.Name}' collides with an existing command; command will not be added.");
                continue;
            }

            _tagsToScriptDictionary.Add(tag, def.Script!);

            var menuItem = new MenuFlyoutItem
            {
                Text = def.DisplayName,
                Tag = tag
            };

            if (!string.IsNullOrEmpty(def.Icon) && 
                Enum.TryParse<Symbol>(def.Icon, out var icon))
            {
                menuItem.Icon = new SymbolIcon(icon);
            }

            menuItem.Click += OnMenuItemClick;
            menuItems.Add(menuItem);
        }
    }

    private ShortcutButton CreateShortcutButton(string? name, string? iconName, string? tooltip)
    {
        var button = new ShortcutButton();

        // Set icon if provided
        if (!string.IsNullOrEmpty(iconName) && 
            Enum.TryParse<Symbol>(iconName, out var icon))
        {
            button.SetIcon(icon);
        }
        else
        {
            // Default to Play icon for shortcuts
            button.SetIcon(Symbol.Play);
        }

        // Set tooltip if provided
        if (!string.IsNullOrEmpty(tooltip))
        {
            button.SetTooltip(tooltip);
        }
        else if (!string.IsNullOrEmpty(name))
        {
            button.SetTooltip(name);
        }

        return button;
    }

    /// <summary>
    /// Called by shortcut buttons on the Project Panel navigation bar.
    /// </summary>
    private void OnShortcutButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is ShortcutButton button && 
            button.Tag is string tag)
        {
            ShortcutClicked?.Invoke(tag);
        }
    }

    /// <summary>
    /// Called by shortcut menu items in the flyout menus.
    /// </summary>
    private void OnMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && 
            menuItem.Tag is string tag)
        {
            ShortcutClicked?.Invoke(tag);
        }
    }
}
