using Celbridge.Host;
using Celbridge.WebHost;

namespace Celbridge.Documents.Views;

/// <summary>
/// Adapts a custom editor's CelbridgeHost to the ICustomEditorChannelHost seam that a channel talks
/// through. The controller marks it disposed during teardown so a channel's background thread that fires
/// an outbound notification after the host is gone no-ops instead of throwing.
/// </summary>
internal sealed class CustomEditorChannelHost : ICustomEditorChannelHost
{
    private readonly CelbridgeHost _host;
    private bool _disposed;

    public CustomEditorChannelHost(CelbridgeHost host)
    {
        _host = host;
    }

    public void MarkDisposed()
    {
        _disposed = true;
    }

    public void AddLocalRpcTarget<T>(T target) where T : class
    {
        _host.AddLocalRpcTarget(target);
    }

    public Task NotifyAsync(string method, object? argument)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        try
        {
            if (argument is null)
            {
                return _host.Rpc.NotifyAsync(method);
            }

            return _host.Rpc.NotifyWithParameterObjectAsync(method, argument);
        }
        catch (ObjectDisposedException)
        {
            // The host was torn down between the disposed check and the call. The notification is moot.
            return Task.CompletedTask;
        }
    }

    public Task<T> InvokeAsync<T>(string method)
    {
        return _host.Rpc.InvokeAsync<T>(method);
    }
}
