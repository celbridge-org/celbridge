#if DEBUG
//#define INCLUDE_PLACEHOLDER_NAVIGATION_BUTTONS
#else
#endif

using Celbridge.UserInterface.ViewModels.Pages;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.UserInterface.Views;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; private set; }

    public LocalizedString HomeString => _stringLocalizer.GetString($"MainPage_Home");
    public LocalizedString NewProjectString => _stringLocalizer.GetString($"MainPage_NewProject");
    public LocalizedString OpenProjectString => _stringLocalizer.GetString($"MainPage_OpenProject");
    public LocalizedString CloseProjectString => _stringLocalizer.GetString($"MainPage_CloseProject");
    public LocalizedString ExplorerString => _stringLocalizer.GetString($"MainPage_Explorer");
    public LocalizedString SearchString => _stringLocalizer.GetString($"MainPage_Search");
    public LocalizedString DebugString => _stringLocalizer.GetString($"MainPage_Debug");
    public LocalizedString RevisionControlString => _stringLocalizer.GetString($"MainPage_RevisionControl");

    private IStringLocalizer _stringLocalizer;
    private IUserInterfaceService _userInterfaceService;

    private Grid _layoutRoot;
    private NavigationView _mainNavigation;
    private Frame _contentFrame;

    public MainPage()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();

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
                    .MenuItems(
                        new NavigationViewItem()
                            .Icon(new SymbolIcon(Symbol.NewFolder))
                            .Tag(MainPageViewModel.NewProjectTag)
                            .Content(NewProjectString),
                        new NavigationViewItem()
                            .Icon(new SymbolIcon(Symbol.OpenLocal))
                            .Tag(MainPageViewModel.OpenProjectTag)
                            .Content(OpenProjectString)
                    )
                    .Content(HomeString),

                new NavigationViewItemSeparator(),

                new NavigationViewItem()
                    .Icon(new SymbolIcon(Symbol.Home))
                    .Tag(MainPageViewModel.HomeTag)
                    .ToolTipService(PlacementMode.Right, null, HomeString)
                    .Content(HomeString),

                new NavigationViewItem()
                    .Icon(new FontIcon()
                            .FontFamily(symbolFontFamily)
                            .Glyph("\uec50")  // File Explorer
                        )
                    .Tag(MainPageViewModel.ExplorerTag)
                    .ToolTipService(PlacementMode.Right, null, ExplorerString)
                    .Content(HomeString)
#if INCLUDE_PLACEHOLDER_NAVIGATION_BUTTONS
                ,new NavigationViewItem()
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
                    .Content(HomeString)

#endif // INCLUDE_PLACEHOLDER_NAVIGATION_BUTTONS
                )
            .Content(_contentFrame);

        _mainNavigation.Loaded += NavigationView_Loaded;

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

    private void NavigationView_Loaded(object sender, RoutedEventArgs e)
    {
        // This now appears to have no effect, - possibly an ordering thing, - but it's behaviour may actually be undesirable.
//        this._mainNavigation.IsPaneOpen = true;
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
        ViewModel.OnMainPage_Loaded();

        // Begin listening for user navigation events
        _mainNavigation.ItemInvoked += OnMainPage_NavigationViewItemInvoked;

        // Ask for our Navigation pain to be open at start.
        //  (This has to be done in code as loading behaviour overrides this.)

        // This now appears to have no effect, - possibly an ordering thing, - but it's behaviour may actually be undesirable.
//        _mainNavigation.IsPaneOpen = true;

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

        _mainNavigation.ItemInvoked -= OnMainPage_NavigationViewItemInvoked;

        Loaded -= OnMainPage_Loaded;
        Unloaded -= OnMainPage_Unloaded;
    }

    private bool OnKeyDown(VirtualKey key)
    {
        // Use the HasFlag method to check if the control key is down.
        // If you just compare with CoreVirtualKeyStates.Down it doesn't work when the key is held down.
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

        // %%% TEST
        // This now appears to cause the main navigation bar to fully open in full mode, which is undesirable.
//        _mainNavigation.IsPaneOpen = true;

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

    private void OnMainPage_NavigationViewItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            ViewModel.SelectNavigationItem(MainPageViewModel.SettingsTag);
            return;
        }

        var item = args.InvokedItemContainer as NavigationViewItem;
        Guard.IsNotNull(item);

        // %%% We now have a menu accessed from our navigation view, so we have a valid case
        //  where the user needs to click on a value without a tag.
        var navigationItemTag = item.Tag;
//        Guard.IsNotNull(navigationItemTag);

        if (navigationItemTag != null)
        {
            var tag = navigationItemTag.ToString();
            Guard.IsNotNullOrEmpty(tag);

            ViewModel.SelectNavigationItem(tag);
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
}
