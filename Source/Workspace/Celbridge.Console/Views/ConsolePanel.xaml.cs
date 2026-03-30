using Celbridge.Commands;
using Celbridge.Console.Services;
using Celbridge.Console.ViewModels;
using Celbridge.Explorer;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.WebView;
using Celbridge.WebView.Services;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Console.Views;

public sealed partial class ConsolePanel : UserControl, IConsolePanel, IConsoleNotifications, IHostInput
{
    private readonly ILogger<ConsolePanel> _logger;
    private readonly ICommandService _commandService;
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IPanelFocusService _panelFocusService;
    private readonly IWebViewFactory _webViewFactory;
    private readonly IKeyboardShortcutService _keyboardShortcutService;

    private string TitleText => _stringLocalizer.GetString("ConsolePanel_Title");

    public ConsolePanelViewModel ViewModel { get; }

    private ITerminal? _terminal;
    private UserInterfaceTheme _currentTheme;
    private WebView2? _consoleWebView;
    private WebViewHostChannel? _hostChannel;
    private ConsoleHost? _consoleHost;
    private DispatcherQueue? _dispatcher;

    public ConsolePanel()
    {
        this.InitializeComponent();

        _logger = ServiceLocator.AcquireService<ILogger<ConsolePanel>>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _panelFocusService = ServiceLocator.AcquireService<IPanelFocusService>();
        _webViewFactory = ServiceLocator.AcquireService<IWebViewFactory>();
        _keyboardShortcutService = ServiceLocator.AcquireService<IKeyboardShortcutService>();

        ViewModel = ServiceLocator.AcquireService<ConsolePanelViewModel>();

        // Monitor theme changes via messenger
        _currentTheme = _userInterfaceService.UserInterfaceTheme;
        _messengerService.Register<ThemeChangedMessage>(this, OnThemeChanged);

        // Listen for console focus requests
        _messengerService.Register<RequestConsoleFocusMessage>(this, OnRequestConsoleFocus);

        this.Loaded += ConsolePanel_Loaded;
    }

    private void ConsolePanel_Loaded(object sender, RoutedEventArgs e)
    {
        // Sync theme when control becomes visible again in case it changed while invisible
        var currentTheme = _userInterfaceService.UserInterfaceTheme;
        if (_currentTheme != currentTheme)
        {
            _currentTheme = currentTheme;
            SendThemeToConsole();
        }
    }

    private void UserControl_GotFocus(object sender, RoutedEventArgs e)
    {
        _panelFocusService.SetFocusedPanel(WorkspacePanel.Console);
    }

    private void OnThemeChanged(object recipient, ThemeChangedMessage message)
    {
        if (_currentTheme != message.Theme)
        {
            _currentTheme = message.Theme;
            SendThemeToConsole();
        }
    }

    private void OnRequestConsoleFocus(object recipient, RequestConsoleFocusMessage message)
    {
        // Use dispatcher to ensure focus happens after any pending layout updates
        _ = this.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, FocusConsole);
    }

    private void TitleBar_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // When user clicks on the title bar, focus the console
        FocusConsole();
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleConsoleMaximized();
    }

    private void TitleBar_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // Double-clicking the title bar toggles maximize/restore
        ViewModel.ToggleConsoleMaximized();
    }

    private void FocusConsole()
    {
        if (_consoleWebView is not { CoreWebView2: not null } || _consoleHost is null)
        {
            return;
        }

        _consoleWebView.Focus(FocusState.Programmatic);
        _ = _consoleHost.FocusAsync();
    }

    private void SendThemeToConsole()
    {
        if (_consoleHost is null)
        {
            return;
        }

        var themeName = _currentTheme == UserInterfaceTheme.Dark ? "dark" : "light";
        _ = _consoleHost.SetThemeAsync(themeName);
    }

    public void RunCommand(string command)
    {
        if (_consoleHost is null)
        {
            _logger.LogWarning("Cannot run command - console host not initialized");
            return;
        }

        var trimmedCommand = command.Trim();
        _ = _consoleHost.InjectCommandAsync(trimmedCommand);
    }

    public async Task<Result> InitializeTerminalWindow(ITerminal terminal)
    {
        _terminal = terminal;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        // Listen for process exit events
        _terminal.ProcessExited += OnTerminalProcessExited;

        // Acquire a pre-configured WebView from the factory
        _consoleWebView = await _webViewFactory.AcquireAsync();

        // Add to the visual tree
        WebViewContainer.Children.Add(_consoleWebView);

        // Hide the "Inspect" context menu option
        var settings = _consoleWebView.CoreWebView2.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = true;

        // Set up JSON-RPC host channel
        _hostChannel = new WebViewHostChannel(_consoleWebView.CoreWebView2);
        var celbridgeHost = new CelbridgeHost(_hostChannel);
        _consoleHost = new ConsoleHost(celbridgeHost);

        // Register this panel as handler for console notifications
        _consoleHost.AddLocalRpcTarget<IConsoleNotifications>(this);
        _consoleHost.AddLocalRpcTarget<IHostInput>(this);
        _consoleHost.StartListening();

        var tcs = new TaskCompletionSource<bool>();
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            _consoleWebView.NavigationCompleted -= Handler;
            tcs.TrySetResult(args.IsSuccess);
        }
        _consoleWebView.NavigationCompleted += Handler;

        _consoleWebView.CoreWebView2.SetVirtualHostNameToFolderMapping("console.celbridge",
            "Celbridge.Console/Web/Terminal",
            CoreWebView2HostResourceAccessKind.Allow);
        _consoleWebView.CoreWebView2.Navigate("http://console.celbridge/index.html");

        // Wait for navigation to complete
        bool success = await tcs.Task;

        if (!success)
        {
            _logger.LogError("Failed to navigate to console HTML page");
            return Result.Fail("Failed to navigate to console HTML page.");
        }

        _logger.LogDebug("Console WebView initialized successfully");

        // Send initial theme to console after navigation completes
        SendThemeToConsole();

        // Clicking weblinks in the console triggers a navigation event.
        // We intercept those navigation events here and instead open the URI in the system browser.
        _consoleWebView.NavigationStarting += (s, args) =>
        {
            args.Cancel = true;
            var uri = args.Uri;
            OpenSystemBrowser(uri);
        };

        _terminal.OutputReceived += (_, output) =>
        {
            _dispatcher.TryEnqueue(() =>
            {
                if (!IsLoaded || _consoleHost is null)
                {
                    // We can't write the queued console output because the console panel has since unloaded.
                    return;
                }

                _ = _consoleHost.WriteAsync(output);
            });
        };

        return Result.Ok();
    }

    private void OpenSystemBrowser(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return;
        }

        _commandService.Execute<IOpenBrowserCommand>(command =>
        {
            command.URL = uri;
        });
    }

    #region IConsoleNotifications

    public void OnConsoleInput(string data)
    {
        _terminal?.Write(data);
    }

    public void OnConsoleResize(int cols, int rows)
    {
        _terminal?.SetSize(cols, rows);
    }

    #endregion

    #region IHostInput

    public void OnKeyboardShortcut(string key, bool ctrlKey, bool shiftKey, bool altKey)
    {
        _keyboardShortcutService.HandleShortcut(key, ctrlKey, shiftKey, altKey);
    }

    #endregion

    private void OnTerminalProcessExited(object? sender, EventArgs e)
    {
        _logger.LogInformation("Console terminal process exited");

        // Delegate handling to the ViewModel
        ViewModel?.OnTerminalProcessExited();
    }

    public void Shutdown()
    {
        _logger.LogDebug("Console panel shutting down");

        _messengerService.UnregisterAll(this);

        if (_terminal != null)
        {
            _terminal.ProcessExited -= OnTerminalProcessExited;
        }

        _consoleHost?.Dispose();
        _consoleHost = null;

        _hostChannel?.Detach();
        _hostChannel = null;

        if (_consoleWebView != null)
        {
            _consoleWebView.Close();
            _consoleWebView = null;
        }

        this.Loaded -= ConsolePanel_Loaded;

        // Cleanup ViewModel
        ViewModel.Cleanup();
    }
}
