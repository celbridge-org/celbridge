using System.Text.Json;
using Celbridge.Host;

namespace Celbridge.Tests.Host;

/// <summary>
/// Mock implementation of IHostChannel for testing.
/// </summary>
public class MockHostChannel : IHostChannel
{
    public List<string> SentMessages { get; } = new();

    public event EventHandler<string>? MessageReceived;
    public event EventHandler? Closed;

    public void PostMessage(string json)
    {
        SentMessages.Add(json);
        MessagePosted?.Invoke();
    }

    /// <summary>
    /// Raises Closed to simulate the underlying transport dropping.
    /// </summary>
    public void SimulateClosed()
    {
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Event raised when a message is posted, for deterministic test synchronization.
    /// </summary>
    public event Action? MessagePosted;

    public void SimulateMessage(string json)
    {
        MessageReceived?.Invoke(this, json);
    }

    public void SimulateRequest(int id, string method, object? parameters = null)
    {
        object request;
        if (parameters != null)
        {
            request = new
            {
                jsonrpc = "2.0",
                method,
                @params = parameters,
                id
            };
        }
        else
        {
            request = new
            {
                jsonrpc = "2.0",
                method,
                id
            };
        }
        SimulateMessage(JsonSerializer.Serialize(request));
    }

    public void SimulateNotification(string method, object? parameters = null)
    {
        object notification;
        if (parameters != null)
        {
            notification = new
            {
                jsonrpc = "2.0",
                method,
                @params = parameters
            };
        }
        else
        {
            notification = new
            {
                jsonrpc = "2.0",
                method
            };
        }
        SimulateMessage(JsonSerializer.Serialize(notification));
    }
}
