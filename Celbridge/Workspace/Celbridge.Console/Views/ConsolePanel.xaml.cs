using Celbridge.Console.ViewModels;
using Celbridge.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Celbridge.Console.Views;

public sealed partial class ConsolePanel : UserControl, IConsolePanel
{
    private ILogger<ConsolePanel> _logger;

    public ConsolePanelViewModel ViewModel { get; }

    private ITerminal? _terminal;

    public ConsolePanel()
    {
        this.InitializeComponent();

        _logger = ServiceLocator.AcquireService<ILogger<ConsolePanel>>();
        ViewModel = ServiceLocator.AcquireService<ConsolePanelViewModel>();
    }

    public async Task<Result> ExecuteCommand(string command, bool logCommand)
    {
        await Task.CompletedTask;
        return Result.Ok();
    }

    public async Task<Result> InitializeTerminalWindow(ITerminal terminal)
    {
        _terminal = terminal;

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
            return Result.Fail($"Failed to terminal HTML page.");
        }

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
}
