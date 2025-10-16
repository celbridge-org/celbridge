namespace Celbridge.Python;

/// <summary>
/// A service for managing JSON-RPC communication with a Python process over named pipes.
/// </summary>
public interface IRpcService : IDisposable
{
    /// <summary>
    /// Returns whether the RPC service is connected to the Python host.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Connects to the Python host JSON-RPC server.
    /// </summary>
    Task<Result> ConnectAsync();

    /// <summary>
    /// Disconnects from the Python host JSON-RPC server.
    /// </summary>
    Task<Result> DisconnectAsync();

    /// <summary>
    /// Invokes a remote method with arguments and return type T on the Python host JSON-RPC server.
    /// </summary>
    /// <returns>A result containing the return value of the invoked Python method or an error if the method failed to execute.</returns>
    Task<Result<T>> InvokeAsync<T>(string method, object? arguments, CancellationToken cancellationToken = default) where T : notnull;

    /// <summary>
    /// Invokes a remote method with no arguments and return type T on the Python host JSON-RPC server.
    /// </summary>
    /// <returns>A result containing the return value of the invoked Python method or an error if the method failed to execute.</returns>
    Task<Result<T>> InvokeAsync<T>(string method, CancellationToken cancellationToken = default) where T : notnull;

    /// <summary>
    /// Invokes a remote method with arguments and no return type on the Python host JSON-RPC server.
    /// </summary>
    /// <returns>A result containing the return value of the invoked Python method or an error if the method failed to execute.</returns>
    Task<Result> InvokeAsync(string method, object? arguments, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes a remote method with no arguments and no return type on the Python host JSON-RPC server.
    /// </summary>
    /// <returns>A result containing the return value of the invoked Python method or an error if the method failed to execute.</returns>
    Task<Result> InvokeAsync(string method, CancellationToken cancellationToken = default);
}
