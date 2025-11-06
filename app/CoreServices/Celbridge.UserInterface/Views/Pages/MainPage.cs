#if DEBUG
//#define INCLUDE_PLACEHOLDER_NAVIGATION_BUTTONS
#else
#endif

using Celbridge.Projects;
using Celbridge.UserInterface.ViewModels.Pages;
using Celbridge.Navigation;
using Celbridge.Workspace;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.UserInterface.Views;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; private set; }

    public LocalizedString HomeString => _stringLocalizer.GetString($"MainPage_Home");
    public LocalizedString NewProjectString => _stringLocalizer.GetString($"MainPage_NewProject");
    public LocalizedString NewProjectTooltipString => _stringLocalizer.GetString($"MainPage_NewProjectTooltip");
    public LocalizedString OpenProjectString => _stringLocalizer.GetString($"MainPage_OpenProject");
    public LocalizedString OpenProjectTooltipString => _stringLocalizer.GetString($"MainPage_OpenProjectTooltip");
    public LocalizedString ReloadProjectString => _stringLocalizer.GetString($"MainPage_ReloadProject");
    public LocalizedString ReloadProjectTooltipString => _stringLocalizer.GetString($"MainPage_ReloadProjectTooltip");
    public LocalizedString CloseProjectString => _stringLocalizer.GetString($"MainPage_CloseProject");
    public LocalizedString ExplorerString => _stringLocalizer.GetString($"MainPage_Explorer");
    public LocalizedString SearchString => _stringLocalizer.GetString($"MainPage_Search");
    public LocalizedString DebugString => _stringLocalizer.GetString($"MainPage_Debug");
    public LocalizedString RevisionControlString => _stringLocalizer.GetString($"MainPage_RevisionControl");
    public LocalizedString CommunityString => _stringLocalizer.GetString($"MainPage_Community");

    private Dictionary<string, string> TagsToScriptDictionary = new();

    private IStringLocalizer _stringLocalizer;
    private IUserInterfaceService _userInterfaceService;
    private IProjectConfigService _projectConfigService;
    private IProjectService _projectService;

    private Grid _layoutRoot;
    private NavigationView _mainNavigation;
    private Frame _contentFrame;
    private List<KeyValuePair<IList<object>, NavigationViewItem>> _userScriptMenuItems = new();

    public MainPage()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        _projectService = ServiceLocator.AcquireService<IProjectService>();
        _projectConfigService = ServiceLocator.AcquireService<IProjectConfigService>();

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
                    .MenuItems(
                        new NavigationViewItem()
                            .Icon(new SymbolIcon(Symbol.NewFolder))
                            .Tag(NavigationConstants.NewProjectTag)
                            .Content(NewProjectString)
                            .ToolTipService(PlacementMode.Right, null, NewProjectTooltipString),
                        new NavigationViewItem()
                            .Icon(new SymbolIcon(Symbol.OpenLocal))
                            .Tag(NavigationConstants.OpenProjectTag)
                            .Content(OpenProjectString)
                            .ToolTipService(PlacementMode.Right, null, OpenProjectTooltipString),
                        new NavigationViewItem()
                            .Icon(new SymbolIcon(Symbol.Refresh))
                            .Tag(NavigationConstants.ReloadProjectTag)
                            .IsEnabled(x => x.Binding(() => ViewModel.IsWorkspaceLoaded))
                            .Content(ReloadProjectString)
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

    private void MainNavigation_Loaded(object sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void OnMainPage_Loaded(object sender, RoutedEventArgs e)
    {
        var mainWindow = _userInterfaceService.MainWindow as Window;
        Guard.IsNotNull(mainWindow);

#if WINDOWS
        // Setup the custom title bar (Windows only)
        var titleBar = new TitleBar();
        _layoutRoot.Children.Add(titleBar);

        mainWindow.ExtendsContentIntoTitleBar = true;
        mainWindow.SetTitleBar(titleBar);

        _userInterfaceService.RegisterTitleBar(titleBar);
#endif

        ViewModel.OnNavigate += OnViewModel_Navigate;
        ViewModel.SelectNavigationItem += SelectNavigationItemByName;
        ViewModel.ReturnCurrentPage += ReturnCurrentPage;
        ViewModel.OnMainPage_Loaded();

        // Begin listening for user navigation events
        _mainNavigation.ItemInvoked += OnMainPage_NavigationViewItemInvoked;

        // Configure user function menu items.
        _projectService.RegisterRebuildUserFunctionsUI(BuildUserFunctionMenuItems);

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

        ViewModel.OnNavigate -= OnViewModel_Navigate;
        ViewModel.SelectNavigationItem -= SelectNavigationItemByName;
        ViewModel.ReturnCurrentPage = ReturnCurrentPage;

        _mainNavigation.ItemInvoked -= OnMainPage_NavigationViewItemInvoked;

        Loaded -= OnMainPage_Loaded;
        Unloaded -= OnMainPage_Unloaded;

        _projectService.UnregisterRebuildUserFunctionsUI(BuildUserFunctionMenuItems);
    }

    private bool OnKeyDown(VirtualKey key)
    {
        // Use the HasFlag method to check if the control key is down.
        // If you just compare with CoreVirtualKeyStates.Down it doesn't work when the key is held down.
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

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
            return Result.Ok();
        }
        return Result.Fail($"Failed to navigate to page type {pageType}");
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

    private void OnMainPage_NavigationViewItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
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
            // Check if tag is a user command tag.
            if (TagsToScriptDictionary.ContainsKey(navigationItemTag.ToString()!))
            {
                var script = TagsToScriptDictionary[navigationItemTag.ToString()!];

                var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
                workspaceWrapper.WorkspaceService.ConsoleService.RunCommand(script);

                return;
            }

            var tag = navigationItemTag.ToString();
            Guard.IsNotNullOrEmpty(tag);

            ViewModel.OnSelectNavigationItem(tag);
        }
    }

    public void Navigate(Type pageType, object parameter)
    {
        Guard.IsNotNull(_contentFrame);

        if (_contentFrame.Content is null ||
            _contentFrame.Content.GetType() != pageType)
        {
            _contentFrame.Navigate(pageType, parameter);
        }
    }

    public Result SelectNavigationItemByName(string navItemName)
    {      
        // %%% Work around for purpose of presentation until the bug with the intended line can be resolved.
        _mainNavigation.SelectedItem = _mainNavigation.MenuItems.ElementAt(3);
//        _mainNavigation.SelectedItem ??= _mainNavigation.FindName(navItemName) as NavigationViewItem;
        return Result.Ok();
    }

    private void BuildUserFunctionMenuItems(object sender, IProjectService.RebuildUserFunctionsUIEventArgs args)
    {
        TagsToScriptDictionary.Clear();
        foreach (var (menuItems, menuItem) in _userScriptMenuItems)
        {
            menuItems.Remove(menuItem);
        }
        _userScriptMenuItems.Clear();

        NavigationBarSection.CustomCommandNode node = args.NavigationBarSection.RootCustomCommandNode;

        AddUserFunctionMenuItems(node, _mainNavigation.MenuItems);
    }

    private void AddUserFunctionMenuItems(NavigationBarSection.CustomCommandNode node, IList<object> menuItems)
    {
        foreach (var (k, v) in node.Nodes)
        {
            var newItem = new NavigationViewItem()
                    .Icon(new SymbolIcon(Symbol.Folder))
                    .Name(k)
                    .Content(k);

            menuItems.Add(newItem);
            _userScriptMenuItems.Add(new KeyValuePair<IList<object>, NavigationViewItem>(menuItems, newItem));
            AddUserFunctionMenuItems(v, newItem.MenuItems);
        }

        foreach (var command in node.CustomCommands)
        {
            Symbol icon = Symbol.Placeholder;
            if (command.Icon is not null)
            {
                if (!Enum.TryParse(command.Icon, out icon))
                {
                    icon = Symbol.Placeholder;
                }
            }

            TagsToScriptDictionary.Add(command.Path!, command.Script!);

            var commandItem = new NavigationViewItem()
                .Icon(new SymbolIcon(icon))
                .ToolTipService(PlacementMode.Right, null, command.ToolTip)
                .Name(command.Name ?? "UserFunction")
                .Content(command.Name ?? "UserFunction")
                .Tag(command.Path!);
            menuItems.Add(commandItem);
            _userScriptMenuItems.Add(new KeyValuePair<IList<object>, NavigationViewItem>(menuItems, commandItem));
        }
    }
}
