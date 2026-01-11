#if DEBUG
//#define INCLUDE_PLACEHOLDER_NAVIGATION_BUTTONS
#else
#endif

using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Settings;
using Celbridge.UserInterface.ViewModels.Pages;
using Celbridge.Navigation;
using Celbridge.Workspace;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.UserInterface.Views;

public partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; private set; }

    public string HomeString => _stringLocalizer.GetString($"MainPage_Home");
    public string NewProjectString => _stringLocalizer.GetString($"MainPage_NewProject");
    public string NewProjectTooltipString => _stringLocalizer.GetString($"MainPage_NewProjectTooltip");
    public string OpenProjectString => _stringLocalizer.GetString($"MainPage_OpenProject");
    public string OpenProjectTooltipString => _stringLocalizer.GetString($"MainPage_OpenProjectTooltip");
    public string ReloadProjectString => _stringLocalizer.GetString($"MainPage_ReloadProject");
    public string ReloadProjectTooltipString => _stringLocalizer.GetString($"MainPage_ReloadProjectTooltip");
    public string CloseProjectString => _stringLocalizer.GetString($"MainPage_CloseProject");
    public string ExplorerString => _stringLocalizer.GetString($"MainPage_Explorer");
    public string SearchString => _stringLocalizer.GetString($"MainPage_Search");
    public string DebugString => _stringLocalizer.GetString($"MainPage_Debug");
    public string RevisionControlString => _stringLocalizer.GetString($"MainPage_RevisionControl");
    public string CommunityString => _stringLocalizer.GetString($"MainPage_Community");


    private Dictionary<string, string> TagsToScriptDictionary = new();

    private IStringLocalizer _stringLocalizer;
    private IUserInterfaceService _userInterfaceService;
    private IProjectService _projectService;
    private IMessengerService _messengerService;
    private readonly ILogger<MainPage> _logger;

    private Grid _layoutRoot;
    private NavigationView _mainNavigation;
    private Frame _contentFrame;
    private List<KeyValuePair<IList<object>, NavigationViewItem>> _shortcutMenuItems = new();
    private string _currentNavigationTag = NavigationConstants.HomeTag;

#if WINDOWS
    private TitleBar? _titleBar;
#endif

    public MainPage()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        _projectService = ServiceLocator.AcquireService<IProjectService>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _logger = ServiceLocator.AcquireService<ILogger<MainPage>>();

        ViewModel = ServiceLocator.AcquireService<MainPageViewModel>();

        _contentFrame = new Frame()
            .Background(ThemeResource.Get<Brush>("ApplicationBackgroundBrush"))
            .Name("ContentFrame");

        var symbolFontFamily = ThemeResource.Get<FontFamily>("SymbolThemeFontFamily");

        _mainNavigation = new NavigationView()
            .Name("MainNavigation")
            .Grid(row: 1)
            .IsPaneToggleButtonVisible(false)
            .Background(ThemeResource.Get<Brush>("PanelBackgroundABrush"))
            .IsBackButtonVisible(NavigationViewBackButtonVisible.Collapsed)
            .PaneDisplayMode(NavigationViewPaneDisplayMode.LeftCompact)
            .MenuItems(
                new NavigationViewItem()
                    .Icon(new SymbolIcon(Symbol.GlobalNavigationButton))
                    .Name("MenuNavigationItem")
                    .SelectsOnInvoked(false)
                    .MenuItems(
                        new NavigationViewItem()
                            .Icon(new SymbolIcon(Symbol.NewFolder))
                            .Tag(NavigationConstants.NewProjectTag)
                            .Content(NewProjectString)
                            .SelectsOnInvoked(false)
                            .ToolTipService(PlacementMode.Right, null, NewProjectTooltipString),
                        new NavigationViewItem()
                            .Icon(new SymbolIcon(Symbol.OpenLocal))
                            .Tag(NavigationConstants.OpenProjectTag)
                            .Content(OpenProjectString)
                            .SelectsOnInvoked(false)
                            .ToolTipService(PlacementMode.Right, null, OpenProjectTooltipString),
                        new NavigationViewItem()
                            .Icon(new SymbolIcon(Symbol.Refresh))
                            .Tag(NavigationConstants.ReloadProjectTag)
                            .IsEnabled(x => x.Binding(() => ViewModel.IsWorkspaceLoaded))
                            .Content(ReloadProjectString)
                            .SelectsOnInvoked(false)
                            .ToolTipService(PlacementMode.Right, null, ReloadProjectTooltipString)
                    )
                    .Content(HomeString),

                new NavigationViewItemSeparator(),

                new NavigationViewItem()
                    .Icon(new SymbolIcon(Symbol.Home))
                    .Name("HomeNavigationItem")
                    .Tag(NavigationConstants.HomeTag)
                    .ToolTipService(PlacementMode.Right, null, HomeString)
                    .Content(HomeString),

                new NavigationViewItem()
                    .Icon(new FontIcon()
                            .FontFamily(symbolFontFamily)
                            .Glyph("\uec50")  // File Explorer
                        )
                    .Name("ExplorerNavigationItem")
                    .IsEnabled(x => x.Binding(() => ViewModel.IsWorkspaceLoaded))
                    .Tag(NavigationConstants.ExplorerTag)
                    .ToolTipService(PlacementMode.Right, null, ExplorerString)
                    .Content(HomeString),
#if INCLUDE_PLACEHOLDER_NAVIGATION_BUTTONS
                new NavigationViewItem()
                    .Icon(new FontIcon()
                            .FontFamily(symbolFontFamily)
                            .Glyph("\ue721")   // Search
                        )
                    .Tag(MainPageViewModel.SearchTag)
                    .ToolTipService(PlacementMode.Right, null, SearchString)
                    .Content(HomeString),

                new NavigationViewItem()
                    .Icon(new FontIcon()
                            .FontFamily(symbolFontFamily)
                            .Glyph("\uebe8")   // Bug
                        )
                    .Tag(MainPageViewModel.DebugTag)
                    .ToolTipService(PlacementMode.Right, null, DebugString)
                    .Content(HomeString),

                new NavigationViewItem()
                    .Icon(new SymbolIcon(Symbol.Upload))
                    .Tag(MainPageViewModel.RevisionControlTag) // GitHub
                    .ToolTipService(PlacementMode.Right, null, RevisionControlString)
                    .Content(HomeString),

#endif // INCLUDE_PLACEHOLDER_NAVIGATION_BUTTONS

                new NavigationViewItemSeparator()

                )
            .FooterMenuItems(
                new NavigationViewItem()
                    .Icon(new FontIcon()
                            .FontFamily(symbolFontFamily)
//                            .Glyph("\ue125")  // Community - Two people
//                            .Glyph("\ue128")  // Community - World
                            .Glyph("\ue12b")  // Community - Globe
                        )
                    .Name("CommunityNavigationItem")
                    .Tag(NavigationConstants.CommunityTag)
                    .ToolTipService(PlacementMode.Right, null, CommunityString)
                    .Content(HomeString)
            )
            .Content(_contentFrame);

        _layoutRoot = new Grid()
            .Name("LayoutRoot")
            .RowDefinitions("Auto, *")
            .Children(_mainNavigation);

        this.DataContext(ViewModel, (page, vm) => page
            .Content(_layoutRoot));

        Loaded += OnMainPage_Loaded;
        Unloaded += OnMainPage_Unloaded;
    }

    private void OnMainPage_Loaded(object sender, RoutedEventArgs e)
    {
        var mainWindow = _userInterfaceService.MainWindow as Window;
        Guard.IsNotNull(mainWindow);

#if WINDOWS
        // Setup the custom title bar (Windows only)
        _titleBar = new TitleBar();
        _layoutRoot.Children.Add(_titleBar);

        mainWindow.ExtendsContentIntoTitleBar = true;
        mainWindow.SetTitleBar(_titleBar);

        // Configure the AppWindow titlebar to use taller caption buttons (48px instead of 32px)
        // This makes the system minimize/maximize/close buttons larger to match the increased titlebar height
        var appWindow = mainWindow.AppWindow;
        if (appWindow?.TitleBar != null)
        {
            appWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;
        }

        _userInterfaceService.RegisterTitleBar(_titleBar);
#endif

        // Register for window layout changes
        _messengerService.Register<WindowLayoutChangedMessage>(this, OnWindowLayoutChanged);

        ViewModel.OnNavigate += OnViewModel_Navigate;
        ViewModel.SelectNavigationItem += SelectNavigationItemByName;
        ViewModel.ReturnCurrentPage += ReturnCurrentPage;
        ViewModel.OnMainPage_Loaded();

        // Begin listening for user navigation events
        _mainNavigation.ItemInvoked += OnMainPage_NavigationViewItemInvoked;

        // Ensure correct initial navigation state
        UpdateNavigationSelection();

        // Configure shortcut menu items.
        _projectService.RegisterRebuildShortcutsUI(BuildShortcutMenuItems);

        // Listen for keyboard input events (required for undo / redo)
#if WINDOWS
        mainWindow.Content.KeyDown += (s, e) =>
        {
            if (OnKeyDown(e.Key))
            {
                e.Handled = true;
            }
        };
#else
        Guard.IsNotNull(mainWindow);
        if (mainWindow.CoreWindow is not null)
        {
            mainWindow.CoreWindow.KeyDown += (s, e) =>
            {
                if (OnKeyDown(e.VirtualKey))
                {
                    e.Handled = true;
                }
            };
        }
#endif
    }

    private void OnMainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnMainPage_Unloaded();

        // Unregister all event handlers to avoid memory leaks

        _messengerService.UnregisterAll(this);

        ViewModel.OnNavigate -= OnViewModel_Navigate;
        ViewModel.SelectNavigationItem -= SelectNavigationItemByName;
        ViewModel.ReturnCurrentPage = ReturnCurrentPage;

        _mainNavigation.ItemInvoked -= OnMainPage_NavigationViewItemInvoked;

        Loaded -= OnMainPage_Loaded;
        Unloaded -= OnMainPage_Unloaded;

        _projectService.UnregisterRebuildShortcutsUI(BuildShortcutMenuItems);
    }

    private void OnWindowLayoutChanged(object recipient, WindowLayoutChangedMessage message)
    {
#if WINDOWS
        // Show/hide the title bar based on window layout
        // In Windowed and FullScreen modes, the title bar is visible
        // In ZenMode and Presenter modes, the title bar is hidden
        if (_titleBar != null)
        {
            bool showTitleBar = message.WindowLayout == WindowLayout.Windowed || 
                                message.WindowLayout == WindowLayout.FullScreen;
            _titleBar.Visibility = showTitleBar ? Visibility.Visible : Visibility.Collapsed;
        }
#endif
    }

    private bool OnKeyDown(VirtualKey key)
    {
        // Use the HasFlag method to check if the control key is down.
        // If you just compare with CoreVirtualKeyStates.Down it doesn't work when the key is held down.
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        // F11 toggles Zen Mode / Fullscreen (universal shortcut)
        if (key == VirtualKey.F11)
        {
            var commandService = ServiceLocator.AcquireService<Celbridge.Commands.ICommandService>();
            commandService.Execute<IToggleZenModeCommand>();
            return true;
        }

        // All platforms redo shortcut
        if (control && shift && key == VirtualKey.Z)
        {
            ViewModel.Redo();
            return true;
        }

#if WINDOWS
        // Windows only redo shortcut
        if (control && key == VirtualKey.Y)
        {
            ViewModel.Redo();
            return true;
        }
#endif

        // All platforms undo shortcut
        if (control && key == VirtualKey.Z)
        {
            ViewModel.Undo();
            return true;
        }

        return false;
    }

    private Result OnViewModel_Navigate(Type pageType, object parameter)
    {
        if (_contentFrame.Content != null &&
            _contentFrame.Content.GetType() == pageType)
        {
            // Already at the requested page, so just early out.
            return Result.Ok();
        }

        if (_contentFrame.Navigate(pageType, parameter))
        {
            // Update navigation state based on the page type
            UpdateNavigationStateForPageType(pageType);
            return Result.Ok();
        }
        return Result.Fail($"Failed to navigate to page type {pageType}");
    }

    private void UpdateNavigationStateForPageType(Type pageType)
    {
        // Map page types to navigation tags
        var pageName = pageType.Name;
        
        string newTag = pageName switch
        {
            "HomePage" => NavigationConstants.HomeTag,
            "WorkspacePage" => NavigationConstants.ExplorerTag,
            "SettingsPage" => NavigationConstants.SettingsTag,
            "CommunityPage" => NavigationConstants.CommunityTag,
            _ => _currentNavigationTag // Keep current if unknown page type
        };

        if (newTag != _currentNavigationTag)
        {
            _currentNavigationTag = newTag;
            UpdateNavigationSelection();
        }
    }

    private string ReturnCurrentPage()
    {
        Page? currentPage = _contentFrame.Content as Page;
        if (currentPage != null)
        {
            return currentPage.Name;
        }
        else
        {
            return "None";
        }
    }

    private void UpdateNavigationSelection()
    {
        // Find the navigation item that matches the current navigation tag
        var itemToSelect = FindNavigationItemByTag(_currentNavigationTag);        
        if (itemToSelect != null)
        {
            _mainNavigation.SelectedItem = itemToSelect;
        }
    }

    private NavigationViewItem? FindNavigationItemByTag(string tag)
    {
        // Check settings
        if (tag == NavigationConstants.SettingsTag)
        {
            return null; // Settings is handled differently
        }

        // Search through menu items
        foreach (var item in _mainNavigation.MenuItems)
        {
            if (item is NavigationViewItem navItem)
            {
                if (navItem.Tag?.ToString() == tag)
                {
                    return navItem;
                }
            }
        }

        // Search through footer items
        foreach (var item in _mainNavigation.FooterMenuItems)
        {
            if (item is NavigationViewItem navItem)
            {
                if (navItem.Tag?.ToString() == tag)
                {
                    return navItem;
                }
            }
        }

        return null;
    }

    private void CloseFlyoutMenus()
    {
        // Close all flyout menus by recursively collapsing expanded items
        CloseFlyoutMenusRecursive(_mainNavigation.MenuItems);
        CloseFlyoutMenusRecursive(_mainNavigation.FooterMenuItems);
    }

    private void CloseFlyoutMenusRecursive(IList<object> menuItems)
    {
        foreach (var item in menuItems)
        {
            if (item is NavigationViewItem navItem)
            {
                if (navItem.MenuItems.Count > 0)
                {
                    navItem.IsExpanded = false;
                    CloseFlyoutMenusRecursive(navItem.MenuItems);
                }
            }
        }
    }

    private void OnMainPage_NavigationViewItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            _currentNavigationTag = NavigationConstants.SettingsTag;
            ViewModel.OnSelectNavigationItem(NavigationConstants.SettingsTag);
            return;
        }

        var item = args.InvokedItemContainer as NavigationViewItem;
        Guard.IsNotNull(item);

        // Note: We now have a menu accessed from our navigation view, so we have a valid case
        //  where the user needs to click on a value without a tag.
        var navigationItemTag = item.Tag;
        if (navigationItemTag != null)
        {
            var tag = navigationItemTag.ToString();
            Guard.IsNotNullOrEmpty(tag);

            // Check if tag is a user command tag (shortcut).
            if (TagsToScriptDictionary.ContainsKey(tag))
            {
                var script = TagsToScriptDictionary[tag];
                if ((script == null) || (script.Length == 0))
                {
                    return;
                }

                // Close flyout menus since shortcut items have SelectsOnInvoked(false)
                CloseFlyoutMenus();

                var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
                workspaceWrapper.WorkspaceService.ConsoleService.RunCommand(script);

                // Restore the correct navigation selection after executing shortcut
                UpdateNavigationSelection();
                return;
            }

            // Check if this is a main menu command (NewProject, OpenProject, ReloadProject)
            if (tag == NavigationConstants.NewProjectTag ||
                tag == NavigationConstants.OpenProjectTag ||
                tag == NavigationConstants.ReloadProjectTag)
            {
                // Close flyout menus since menu items have SelectsOnInvoked(false)
                CloseFlyoutMenus();

                // Execute the command
                ViewModel.OnSelectNavigationItem(tag);

                // Restore the correct navigation selection
                UpdateNavigationSelection();
                return;
            }

            // This is a regular navigation item
            // Update the current navigation state
            _currentNavigationTag = tag;
            ViewModel.OnSelectNavigationItem(tag);
            
            // Ensure selection reflects the new state
            UpdateNavigationSelection();
        }
    }

    public Result SelectNavigationItemByName(string navItemName)
    {
        // Map the navigation item name to a tag
        string tag = navItemName switch
        {
            "HomeNavigationItem" => NavigationConstants.HomeTag,
            "ExplorerNavigationItem" => NavigationConstants.ExplorerTag,
            "CommunityNavigationItem" => NavigationConstants.CommunityTag,
            _ => NavigationConstants.HomeTag
        };

        _currentNavigationTag = tag;
        UpdateNavigationSelection();
        return Result.Ok();
    }

    private void BuildShortcutMenuItems(object sender, IProjectService.RebuildShortcutsUIEventArgs args)
    {
        TagsToScriptDictionary.Clear();
        foreach (var (menuItems, menuItem) in _shortcutMenuItems)
        {
            menuItems.Remove(menuItem);
        }
        _shortcutMenuItems.Clear();

        NavigationBarSection.CustomCommandNode node = args.NavigationBarSection.RootCustomCommandNode;

        AddShortcutMenuItems(node, _mainNavigation.MenuItems);
    }

    private void AddShortcutMenuItems(NavigationBarSection.CustomCommandNode node, IList<object> menuItems)
    {
        Dictionary<string, NavigationViewItem> newNodes = new();
        Dictionary<string, string > pathToScriptDictionary = new();

        foreach (var (k, v) in node.Nodes)
        {
            var newItem = new NavigationViewItem()
                    .Name(k)
                    .Content(k)
                    .SelectsOnInvoked(false)
                    .IsEnabled(x => x.Binding(() => ViewModel.IsWorkspacePageActive));

            menuItems.Add(newItem);
            string newPath = v.Path + (v.Path.Length > 0 ? "." : "") + k;
            newNodes.Add(newPath, newItem);
            _shortcutMenuItems.Add(new KeyValuePair<IList<object>, NavigationViewItem>(menuItems, newItem));
            AddShortcutMenuItems(v, newItem.MenuItems);
        }

        foreach (var command in node.CustomCommands)
        {
            // Check for another script already declared with the same path.
            if (pathToScriptDictionary.ContainsKey(command.Path!))
            {
                // Issue a warning, and skip on to the next command.
                _logger.LogWarning($"Shortcut command '{command.Name}' at path '{command.Path}' collides with an existing command; command will not be added and script will not be run.");
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

            // Check if this collides with an folder node.
            if (newNodes.ContainsKey(command.Path!))
            {
                // It does!
                NavigationViewItem item = newNodes[command.Path!];
                if ((command.ToolTip != null) && (command.ToolTip.Length > 0))
                {
                    item.ToolTipService(PlacementMode.Right, null, command.ToolTip);
                }

                if (icon.HasValue)
                {
                    item.Icon = new SymbolIcon(icon.Value);
                }

                if ((command.Name != null) && (command.Name.Length > 0))
                {
                    item.Content = command.Name;
                }

                // Issue a warning if a script has been supplied, that as this path overloads a folder it won't be implemented as a command.
                _logger.LogWarning($"Shortcut command '{command.Name}' at path '{command.Path}' collides with an existing folder node; command will not be added and script will not be run." );

                // Skip adding a command as we're just updating the existing folder node.
                continue;
            }

            TagsToScriptDictionary.Add(command.Path!, command.Script!);

            var commandItem = new NavigationViewItem()
                .ToolTipService(PlacementMode.Right, null, command.ToolTip)
                .Name(command.Name ?? "Shortcut")
                .Content(command.Name ?? "Shortcut")
                .Tag(command.Path!)
                .SelectsOnInvoked(false)
                .IsEnabled(x => x.Binding(() => ViewModel.IsWorkspacePageActive));
            
            if (icon.HasValue)
            {
                commandItem.Icon(new SymbolIcon(icon.Value));
            }
            
            menuItems.Add(commandItem);
            _shortcutMenuItems.Add(new KeyValuePair<IList<object>, NavigationViewItem>(menuItems, commandItem));
        }
    }
}
