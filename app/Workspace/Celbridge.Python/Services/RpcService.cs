using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace Celbridge.Python.Services;

/// <summary>
/// Manages JSON-RPC communication with a Python process over named pipes.
/// </summary>
public class RpcService : IRpcService
{
    private readonly ILogger<RpcService> _logger;
    private readonly string _pipeName;
    private readonly SemaphoreSlim _lock = new(1, 1);
    
    private NamedPipeClientStream? _pipeStream;
    private JsonRpc? _rpc;
    private volatile bool _disposed; // Atomic reads/writes for thread safety

    public bool IsConnected
    {
        get
        {
            var rpc = _rpc; // Take snapshot for thread safety
            return rpc != null && !rpc.IsDisposed;
        }
    }

    public RpcService(ILogger<RpcService> logger, string pipeName)
    {
        _logger = logger;
        _pipeName = pipeName;
    }

    public async Task<Result> ConnectAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _lock.WaitAsync(cts.Token);
        try
        {
            if (_disposed)
            {
                return Result.Fail("RpcService has been disposed");
            }

            if (IsConnected)
            {
                return Result.Ok();
            }

            _logger.LogInformation("Connecting to Python RPC server on pipe: {PipeName}", _pipeName);

            try
            {
                // Create named pipe client stream
                _pipeStream = new NamedPipeClientStream(
                    serverName: ".",
                    pipeName: _pipeName,
                    direction: PipeDirection.InOut,
                    options: PipeOptions.Asynchronous);

                // Connect to the pipe with timeout
                await _pipeStream.ConnectAsync();

                // Attach JSON-RPC (automatically starts listening)
                _rpc = JsonRpc.Attach(_pipeStream);

                // Listen for disconnection
                _rpc.Disconnected += OnRpcDisconnected;

                _logger.LogInformation("Successfully connected to Python RPC server");
            }
            catch (OperationCanceledException ex)
            {
                // Clean up any partially created resources
                if (_rpc != null)
                {
                    _rpc.Disconnected -= OnRpcDisconnected;
                    _rpc.Dispose();
                    _rpc = null;
                }
                _pipeStream?.Dispose();
                _pipeStream = null;
                
                return Result.Fail("Connection to Python RPC server was cancelled or timed out")
                    .WithException(ex);
            }
            catch (TimeoutException ex)
            {
                return Result.Fail("Timeout while connecting to Python RPC server")
                    .WithException(ex);
            }
            catch (IOException ex)
            {
                return Result.Fail($"Failed to connect to named pipe '{_pipeName}'")
                    .WithException(ex);
            }
            catch (Exception ex)
            {
                return Result.Fail("An error occurred while connecting to Python RPC server")
                    .WithException(ex);
            }
        }
        finally
        {
            _lock.Release();
        }

        var versionResult = await GetCelbridgeVersionAsync();
        if (versionResult.IsSuccess)
        {
            var version = versionResult.Value;
            _logger.LogInformation("Connected to celbridge Python package version: {Version}", version);
        }

        return Result.Ok();
    }

    public async Task<Result<string>> GetCelbridgeVersionAsync()
    {
        try
        {
            // Check if connected
            if (!IsConnected)
            {
                return Result<string>.Fail("RPC service is not connected to Python process");
            }

            // Invoke the "version" method with no arguments
            var versionResult = await InvokeAsync<string>("version");

            if (versionResult.IsFailure)
            {
                _logger.LogError("Failed to get celbridge version: {Error}", versionResult.Error);
                return versionResult;
            }

            _logger.LogInformation("Celbridge Python package version: {Version}", versionResult.Value);
            return versionResult;
        }
        catch (Exception ex)
        {
            return Result<string>.Fail("An error occurred while getting celbridge version")
                .WithException(ex);
        }
    }

    public async Task<Result> DisconnectAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _lock.WaitAsync(cts.Token);
        try
        {
            if (!IsConnected)
            {
                return Result.Ok();
            }

            _logger.LogInformation("Disconnecting from Python RPC server");

            try
            {
                if (_rpc != null)
                {
                    _rpc.Disconnected -= OnRpcDisconnected;
                    await _rpc.Completion; // Wait for any pending operations
                    _rpc.Dispose();
                    _rpc = null;
                }

                if (_pipeStream != null)
                {
                    await _pipeStream.DisposeAsync();
                    _pipeStream = null;
                }

                _logger.LogInformation("Successfully disconnected from Python RPC server");
                return Result.Ok();
            }
            catch (Exception ex)
            {
                return Result.Fail("An error occurred while disconnecting from Python RPC server")
                    .WithException(ex);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Result<T>> InvokeAsync<T>(string method, object? arguments, CancellationToken cancellationToken = default) where T : notnull
    {
        var result = await InvokeInternalAsync<T>(method, arguments, cancellationToken);
        if (result.IsFailure)
        {
            return Result<T>.Fail().WithErrors(result);
        }

        return Result<T>.Ok(result.Value);
    }

    public async Task<Result<T>> InvokeAsync<T>(string method, CancellationToken cancellationToken = default) where T : notnull
    {
        return await InvokeAsync<T>(method, null, cancellationToken);
    }

    public async Task<Result> InvokeAsync(string method, object? arguments, CancellationToken cancellationToken = default)
    {
        var result = await InvokeInternalAsync<object>(method, arguments, cancellationToken);
        if (result.IsFailure)
        {
            return Result.Fail().WithErrors(result);
        }

        return Result.Ok();
    }

    public async Task<Result> InvokeAsync(string method, CancellationToken cancellationToken = default)
    {
        return await InvokeAsync(method, null, cancellationToken);
    }

    private async Task<Result<T>> InvokeInternalAsync<T>(string method, object? arguments, CancellationToken cancellationToken) where T : notnull
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_disposed)
            {
                return Result<T>.Fail("RpcService has been disposed");
            }

            if (!IsConnected || _rpc == null)
            {
                return Result<T>.Fail("Not connected to Python RPC server");
            }

            if (string.IsNullOrWhiteSpace(method))
            {
                return Result<T>.Fail("Method name cannot be null or empty");
            }

            try
            {
                _logger.LogDebug("Invoking remote method: {Method}", method);

                T result;
                if (arguments == null)
                {
                    result = await _rpc.InvokeWithCancellationAsync<T>(method, cancellationToken: cancellationToken);
                }
                else
                {
                    result = await _rpc.InvokeWithCancellationAsync<T>(method, arguments: new[] { arguments }, cancellationToken: cancellationToken);
                }

                _logger.LogDebug("Successfully invoked remote method: {Method}", method);
                return Result<T>.Ok(result);
            }
            catch (RemoteInvocationException ex)
            {
                return Result<T>.Fail($"Remote method '{method}' threw an exception: {ex.Message}")
                    .WithException(ex);
            }
            catch (RemoteMethodNotFoundException ex)
            {
                return Result<T>.Fail($"Remote method '{method}' not found")
                    .WithException(ex);
            }
            catch (OperationCanceledException ex)
            {
                return Result<T>.Fail($"Invocation of remote method '{method}' was cancelled")
                    .WithException(ex);
            }
            catch (Exception ex)
            {
                return Result<T>.Fail($"An error occurred while invoking remote method '{method}'")
                    .WithException(ex);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private void OnRpcDisconnected(object? sender, JsonRpcDisconnectedEventArgs e)
    {
        if (e.Exception != null)
        {
            _logger.LogWarning(e.Exception, "RPC connection disconnected unexpectedly");
        }
        else
        {
            _logger.LogInformation("RPC connection disconnected");
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                if (_lock.Wait(TimeSpan.FromSeconds(5)))
                {
                    try
                    {
                        if (_rpc != null)
                        {
                            _rpc.Disconnected -= OnRpcDisconnected;
                            _rpc.Dispose();
                            _rpc = null;
                        }

                        _pipeStream?.Dispose();
                        _pipeStream = null;
                    }
                    finally
                    {
                        _lock.Release();
                    }
                }
                else
                {
                    _logger.LogWarning("Failed to acquire lock during disposal, forcing cleanup");
                    // Force cleanup without lock
                    if (_rpc != null)
                    {
                        _rpc.Disconnected -= OnRpcDisconnected;
                        _rpc.Dispose();
                        _rpc = null;
                    }

                    _pipeStream?.Dispose();
                    _pipeStream = null;
                }

                _lock.Dispose();
            }

            _disposed = true;
        }
    }

    ~RpcService()
    {
        Dispose(false);
    }
}
