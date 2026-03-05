using Celbridge.Host;

namespace Celbridge.Console.Services;

/// <summary>
/// JSON-RPC method names for console operations.
/// </summary>
public static class ConsoleRpcMethods
{
    // Host to client (outgoing notifications)
    public const string Write = "console/write";
    public const string Focus = "console/focus";
    public const string SetTheme = "console/setTheme";
    public const string InjectCommand = "console/injectCommand";

    // Client to host (incoming notifications)
    public const string Input = "console/input";
    public const string Resize = "console/resize";
}

/// <summary>
/// Console-specific host facade that provides a clean API for console RPC operations.
/// Wraps CelbridgeHost and uses JSON-RPC notifications for console communication.
/// </summary>
public class ConsoleHost : IDisposable
{
    private readonly CelbridgeHost _host;
    private bool _disposed;

    public ConsoleHost(CelbridgeHost host)
    {
        _host = host;
    }

    /// <summary>
    /// Registers a target object that implements RPC methods.
    /// </summary>
    public void AddLocalRpcTarget<T>(T target) where T : class
    {
        _host.AddLocalRpcTarget(target);
    }

    /// <summary>
    /// Starts listening for incoming RPC messages.
    /// </summary>
    public void StartListening()
    {
        _host.StartListening();
    }

    /// <summary>
    /// Writes text to the console.
    /// </summary>
    public Task WriteAsync(string text)
    {
        return _host.Rpc.NotifyWithParameterObjectAsync(ConsoleRpcMethods.Write, new { text });
    }

    /// <summary>
    /// Focuses the console input.
    /// </summary>
    public Task FocusAsync()
    {
        return _host.Rpc.NotifyAsync(ConsoleRpcMethods.Focus);
    }

    /// <summary>
    /// Sets the console theme.
    /// </summary>
    public Task SetThemeAsync(string theme)
    {
        return _host.Rpc.NotifyWithParameterObjectAsync(ConsoleRpcMethods.SetTheme, new { theme });
    }

    /// <summary>
    /// Injects a command into the console as if the user typed it.
    /// </summary>
    public Task InjectCommandAsync(string command)
    {
        return _host.Rpc.NotifyWithParameterObjectAsync(ConsoleRpcMethods.InjectCommand, new { command });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host.Dispose();
    }
}
