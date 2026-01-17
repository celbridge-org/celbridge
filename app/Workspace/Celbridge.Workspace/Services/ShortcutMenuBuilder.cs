using Celbridge.Logging;
using Celbridge.Projects;

namespace Celbridge.Workspace.Services;

/// <summary>
/// Helper class for building custom shortcut buttons in the navigation bar.
/// </summary>
public class ShortcutMenuBuilder
{
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
    /// Builds shortcut buttons from the navigation bar section configuration.
    /// </summary>
    public bool BuildShortcutButtons(NavigationBarSection.CustomCommandNode rootNode, StackPanel panel)
    {
        bool hasShortcuts = rootNode.Nodes.Count > 0 || rootNode.CustomCommands.Count > 0;
        
        if (!hasShortcuts)
        {
            return false;
        }

        AddShortcutButtons(rootNode, panel);
        return true;
    }

    private void AddShortcutButtons(NavigationBarSection.CustomCommandNode node, StackPanel panel)
    {
        // Collect the paths of folder nodes so we can skip commands that are just metadata for folders
        var folderPaths = new HashSet<string>();
        foreach (var (nodeName, childNode) in node.Nodes)
        {
            // Build the path for this folder node
            var folderPath = node.Path + (node.Path.Length > 0 ? "." : "") + nodeName;
            folderPaths.Add(folderPath);
        }

        // Add folder nodes as buttons with flyout menus
        foreach (var (nodeName, childNode) in node.Nodes)
        {
            // Find the command definition for this folder node (to get icon/tooltip)
            var folderPath = node.Path + (node.Path.Length > 0 ? "." : "") + nodeName;
            var folderCommand = node.CustomCommands.FirstOrDefault(c => c.Path == folderPath);
            
            var button = CreateShortcutButton(
                folderCommand?.Name ?? nodeName, 
                folderCommand?.Icon, 
                folderCommand?.ToolTip);
            
            // Create a flyout menu for the child items
            var flyout = new MenuFlyout();
            flyout.Placement = FlyoutPlacementMode.RightEdgeAlignedTop;
            AddMenuItems(childNode, flyout.Items);
            button.Flyout = flyout;
            
            panel.Children.Add(button);
        }

        // Add direct command items as buttons (skip commands that are metadata for folder nodes)
        foreach (var command in node.CustomCommands)
        {
            // Skip if this command is just metadata for a folder node
            if (folderPaths.Contains(command.Path!))
            {
                continue;
            }

            if (_tagsToScriptDictionary.ContainsKey(command.Path!))
            {
                _logger.LogWarning($"Shortcut command '{command.Name}' at path '{command.Path}' collides with an existing command; command will not be added.");
                continue;
            }

            _tagsToScriptDictionary.Add(command.Path!, command.Script!);

            var button = CreateShortcutButton(command.Name, command.Icon, command.ToolTip);
            button.Tag = command.Path;
            button.Click += OnShortcutButtonClick;
            
            panel.Children.Add(button);
        }
    }

    private void AddMenuItems(NavigationBarSection.CustomCommandNode node, IList<MenuFlyoutItemBase> menuItems)
    {
        // Collect the paths of folder nodes so we can skip commands that are just metadata for folders
        var folderPaths = new HashSet<string>();
        foreach (var (nodeName, childNode) in node.Nodes)
        {
            var folderPath = node.Path + (node.Path.Length > 0 ? "." : "") + nodeName;
            folderPaths.Add(folderPath);
        }

        // Add sub-folder nodes as sub-menus
        foreach (var (nodeName, childNode) in node.Nodes)
        {
            var subMenu = new MenuFlyoutSubItem
            {
                Text = nodeName
            };
            
            // Find the command definition for this folder node (to get icon/tooltip)
            var folderPath = node.Path + (node.Path.Length > 0 ? "." : "") + nodeName;
            var folderCommand = node.CustomCommands.FirstOrDefault(c => c.Path == folderPath);
            if (folderCommand != null)
            {
                if (!string.IsNullOrEmpty(folderCommand.Name))
                {
                    subMenu.Text = folderCommand.Name;
                }
                if (!string.IsNullOrEmpty(folderCommand.Icon) && Enum.TryParse<Symbol>(folderCommand.Icon, out var icon))
                {
                    subMenu.Icon = new SymbolIcon(icon);
                }
            }
            
            AddMenuItems(childNode, subMenu.Items);
            menuItems.Add(subMenu);
        }

        // Add command items (skip commands that are metadata for folder nodes)
        foreach (var command in node.CustomCommands)
        {
            // Skip if this command is just metadata for a folder node
            if (folderPaths.Contains(command.Path!))
            {
                continue;
            }

            if (_tagsToScriptDictionary.ContainsKey(command.Path!))
            {
                _logger.LogWarning($"Shortcut command '{command.Name}' at path '{command.Path}' collides with an existing command; command will not be added.");
                continue;
            }

            _tagsToScriptDictionary.Add(command.Path!, command.Script!);

            var menuItem = new MenuFlyoutItem
            {
                Text = command.Name ?? "Shortcut",
                Tag = command.Path
            };

            if (!string.IsNullOrEmpty(command.Icon) && Enum.TryParse<Symbol>(command.Icon, out var icon))
            {
                menuItem.Icon = new SymbolIcon(icon);
            }

            menuItem.Click += OnMenuItemClick;
            menuItems.Add(menuItem);
        }
    }

    private Button CreateShortcutButton(string? name, string? iconName, string? tooltip)
    {
        var button = new Button
        {
            Width = 40,
            Height = 40,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // Set icon if provided
        if (!string.IsNullOrEmpty(iconName) && Enum.TryParse<Symbol>(iconName, out var icon))
        {
            button.Content = new SymbolIcon(icon);
        }
        else
        {
            // Default to Play icon for shortcuts
            button.Content = new SymbolIcon(Symbol.Play);
        }

        // Set tooltip if provided
        if (!string.IsNullOrEmpty(tooltip))
        {
            ToolTipService.SetToolTip(button, tooltip);
            ToolTipService.SetPlacement(button, PlacementMode.Right);
        }
        else if (!string.IsNullOrEmpty(name))
        {
            ToolTipService.SetToolTip(button, name);
            ToolTipService.SetPlacement(button, PlacementMode.Right);
        }

        return button;
    }

    private void OnShortcutButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tag)
        {
            ShortcutClicked?.Invoke(tag);
        }
    }

    private void OnMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is string tag)
        {
            ShortcutClicked?.Invoke(tag);
        }
    }
}
