using Celbridge.Logging;
using Celbridge.UserInterface.ViewModels.Pages;
using Celbridge.Navigation;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.UserInterface.Views;

public partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; private set; }

    private IUserInterfaceService _userInterfaceService;
    private IMessengerService _messengerService;
    private readonly ILogger<MainPage> _logger;

    private Grid _layoutRoot;
    private Frame _contentFrame;

#if WINDOWS
    private TitleBar? _titleBar;
#endif

    public MainPage()
    {
        _userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _logger = ServiceLocator.AcquireService<ILogger<MainPage>>();

        ViewModel = ServiceLocator.AcquireService<MainPageViewModel>();

        _contentFrame = new Frame()
            .Background(ThemeResource.Get<Brush>("ApplicationBackgroundBrush"))
            .Name("ContentFrame");

        _layoutRoot = new Grid()
            .Name("LayoutRoot")
            .RowDefinitions("Auto, *")
            .Children(_contentFrame);

        // Position the content frame in the second row (below the title bar)
        Grid.SetRow(_contentFrame, 1);

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

        // Register for window mode changes
        _messengerService.Register<WindowModeChangedMessage>(this, OnWindowLayoutChanged);

        ViewModel.OnNavigate += OnViewModel_Navigate;
        ViewModel.ReturnCurrentPage += ReturnCurrentPage;
        ViewModel.OnMainPage_Loaded();

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

        // Prevent focus indicators from appearing on random UI elements at startup
        // by setting focus to a non-interactive element (the content frame)
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _contentFrame.Focus(FocusState.Programmatic);
        });
    }

    private void OnMainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnMainPage_Unloaded();

        // Unregister all event handlers to avoid memory leaks

        _messengerService.UnregisterAll(this);

        ViewModel.OnNavigate -= OnViewModel_Navigate;
        ViewModel.ReturnCurrentPage -= ReturnCurrentPage;

        Loaded -= OnMainPage_Loaded;
        Unloaded -= OnMainPage_Unloaded;
    }

    private void OnWindowLayoutChanged(object recipient, WindowModeChangedMessage message)
    {
#if WINDOWS
        // Show/hide the title bar based on window mode
        // In Windowed, FullScreen, and ZenMode modes, the title bar is visible
        // In Presenter mode, the title bar is hidden
        if (_titleBar != null)
        {
            bool showTitleBar = message.WindowMode == WindowMode.Windowed || 
                                message.WindowMode == WindowMode.FullScreen ||
                                message.WindowMode == WindowMode.ZenMode;
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

        // F11 shortcut toggles fullscreen mode
        if (key == VirtualKey.F11)
        {
            var commandService = ServiceLocator.AcquireService<Celbridge.Commands.ICommandService>();
            commandService.Execute<ISetLayoutCommand>(command =>
            {
                command.Transition = LayoutTransition.ToggleLayout;
            });

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
}
