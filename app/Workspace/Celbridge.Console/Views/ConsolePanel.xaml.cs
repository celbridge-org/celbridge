using Celbridge.Commands;
using Celbridge.Console.ViewModels;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;
using System.Runtime.InteropServices;

namespace Celbridge.Console.Views;

public sealed partial class ConsolePanel : UserControl, IConsolePanel
{
    private readonly ILogger<ConsolePanel> _logger;
    private readonly ICommandService _commandService;
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly IMessengerService _messengerService;

    public ConsolePanelViewModel ViewModel { get; }

    private ITerminal? _terminal;
    private UserInterfaceTheme _currentTheme;

    public ConsolePanel()
    {
        this.InitializeComponent();

        _logger = ServiceLocator.AcquireService<ILogger<ConsolePanel>>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        ViewModel = ServiceLocator.AcquireService<ConsolePanelViewModel>();

        // Monitor theme changes via messenger
        _currentTheme = _userInterfaceService.UserInterfaceTheme;
        _messengerService.Register<ThemeChangedMessage>(this, OnThemeChanged);

        this.Loaded += ConsolePanel_Loaded;
    }

    private void ConsolePanel_Loaded(object sender, RoutedEventArgs e)
    {
        // Sync theme when control becomes visible again in case it changed while invisible
        var currentTheme = _userInterfaceService.UserInterfaceTheme;
        if (_currentTheme != currentTheme)
        {
            _currentTheme = currentTheme;
            SendThemeToTerminal();
        }
    }

    private void OnThemeChanged(object recipient, ThemeChangedMessage message)
    {
        if (_currentTheme != message.Theme)
        {
            _currentTheme = message.Theme;
            SendThemeToTerminal();
        }
    }

    private void SendThemeToTerminal()
    {
        if (TerminalWebView?.CoreWebView2 != null)
        {
            var themeMessage = $"theme_change,{(_currentTheme == UserInterfaceTheme.Dark ? "dark" : "light")}";
            try
            {
                TerminalWebView.CoreWebView2.PostWebMessageAsString(themeMessage);
            }
            catch (COMException ex)
            {
                _logger.LogWarning(ex, "Failed to send theme change message to terminal");
            }
        }
    }

    public async Task<Result> ExecuteCommand(string command, bool logCommand)
    {
        await Task.CompletedTask;
        return Result.Ok();
    }

    public async Task<Result> InitializeTerminalWindow(ITerminal terminal)
    {
        _terminal = terminal;

        // Listen for process exit events
        _terminal.ProcessExited += OnTerminalProcessExited;

        await TerminalWebView.EnsureCoreWebView2Async();

        // Hide the "Inspect" context menu option
        var settings = TerminalWebView.CoreWebView2.Settings;
        settings.AreDevToolsEnabled = false;                 
        settings.AreDefaultContextMenusEnabled = true;

        var tcs = new TaskCompletionSource<bool>();
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            TerminalWebView.NavigationCompleted -= Handler;
            tcs.TrySetResult(args.IsSuccess);
        }
        TerminalWebView.NavigationCompleted += Handler;

        // Register for messages now so that we will get notified when the terminal first resizes during init.
        TerminalWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

        TerminalWebView.CoreWebView2.SetVirtualHostNameToFolderMapping("Terminal",
            "Celbridge.Console/Assets/Terminal",
            CoreWebView2HostResourceAccessKind.Allow);
        TerminalWebView.CoreWebView2.Navigate("http://Terminal/index.html");

        // Wait for navigation to complete
        bool success = await tcs.Task;

        if (!success) 
        {
            return Result.Fail($"Failed to navigate to terminal HTML page.");
        }

        // Send initial theme to terminal after navigation completes
        SendThemeToTerminal();

        // Clicking weblinks in the terminal triggers a navigation event.
        // We intercept those navigation events here and instead open the URI in the system browser.
        TerminalWebView.NavigationStarting += (s, args) =>
        {
            args.Cancel = true;
            var uri = args.Uri;
            OpenSystemBrowser(uri);
        };

        DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();

        _terminal.OutputReceived += (_, output) =>
        {
            dispatcher.TryEnqueue(() =>
            {
                if (!IsLoaded)
                {
                    // We can't write the queued console input because the console panel has since unloaded.
                    // At this point there's no way to handle this input so we can just ignore it.
                    return;
                }

                SendToTerminalAsync(output);

                // We use the keyboard interrupt as a hacky way to inject commands from outside the REPL.
                if (output == "\u001b[?12l")
                {
                    var command = _terminal.CommandBuffer;
                    if (!string.IsNullOrEmpty(command))
                    {
                        _terminal.Write($"{command}\n");
                        _terminal.CommandBuffer = string.Empty;
                    }
                }
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

    private void CoreWebView2_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        string message = args.TryGetWebMessageAsString();

        if (_terminal is not null &&
            !string.IsNullOrEmpty(message))
        {
            if (message.StartsWith("console_size,"))
            {
                var fields = message.Split(',');
                if (fields.Length == 3)
                {
                    var cols = int.Parse(fields[1]);
                    var rows = int.Parse(fields[2]);

                    _terminal.SetSize(cols, rows);
                    return;
                }
            }

            _terminal.Write(message);
        }
    }

    private void SendToTerminalAsync(string text)
    {
        try
        {
            TerminalWebView.CoreWebView2.PostWebMessageAsString(text);
        }
        catch (COMException ex)
        {
            // Speculative fix for a rare crash on application exit.
            _logger.LogWarning(ex, "An error occurred when posting a message to WebView2");
        }
    }

    private void OnTerminalProcessExited(object? sender, EventArgs e)
    {
        // Delegate handling to the ViewModel
        ViewModel?.OnTerminalProcessExited();
    }

    public void Shutdown()
    {
        _messengerService.UnregisterAll(this);

        if (_terminal != null)
        {
            _terminal.ProcessExited -= OnTerminalProcessExited;
        }

        if (TerminalWebView?.CoreWebView2 != null)
        {
            TerminalWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
        }

        if (TerminalWebView != null)
        {
            TerminalWebView.Close();
        }

        this.Loaded -= ConsolePanel_Loaded;

        // Cleanup ViewModel
        ViewModel.Cleanup();
    }
}
