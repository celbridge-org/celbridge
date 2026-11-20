using System.IO.Pipelines;
using System.Text.Json.Nodes;
using Celbridge.LanguageServers.Services;

namespace Celbridge.Tests.LanguageServers;

/// <summary>
/// A language server process for tests, on a pair of in-memory streams. A test reads the language client's
/// messages, and writes the server's messages.
/// </summary>
internal sealed class FakeLanguageServer : ILanguageServerProcess
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    private readonly Pipe _clientToServer = new();
    private readonly Pipe _serverToClient = new();
    private readonly LspMessageStream _serverSide;
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeLanguageServer()
    {
        Input = _clientToServer.Writer.AsStream();
        Output = _serverToClient.Reader.AsStream();

        _serverSide = new LspMessageStream(_serverToClient.Writer.AsStream(), _clientToServer.Reader.AsStream());
    }

    public Stream Input { get; }

    public Stream Output { get; }

    public Task Exited => _exited.Task;

    public bool WasKilled { get; private set; }

    public bool WasDisposed { get; private set; }

    /// <summary>
    /// Returns the next message that the language client sent to the server.
    /// </summary>
    public async Task<JsonObject> ReadAsync()
    {
        using var timeoutSource = new CancellationTokenSource(ReadTimeout);
        var message = await _serverSide.ReadAsync(timeoutSource.Token);
        message.Should().NotBeNull("the language client should send another message");

        return message!;
    }

    /// <summary>
    /// Returns the next message with the given method, and skips the messages before it.
    /// </summary>
    public async Task<JsonObject> ReadMethodAsync(string method)
    {
        while (true)
        {
            var message = await ReadAsync();
            if ((string?)message["method"] == method)
            {
                return message;
            }
        }
    }

    /// <summary>
    /// Sends one message from the server.
    /// </summary>
    public Task SendAsync(JsonObject message)
    {
        message["jsonrpc"] = "2.0";

        return _serverSide.WriteAsync(message.ToJsonString(), CancellationToken.None);
    }

    /// <summary>
    /// Answers a request from the language client with a result.
    /// </summary>
    public Task RespondAsync(JsonObject request, JsonNode? result)
    {
        var response = new JsonObject
        {
            ["id"] = request["id"]!.DeepClone(),
            ["result"] = result
        };

        return SendAsync(response);
    }

    /// <summary>
    /// Answers the initialize request with the capabilities, and reads the initialized notification.
    /// </summary>
    public async Task<JsonObject> CompleteInitializeAsync(JsonObject capabilities)
    {
        var initializeRequest = await ReadMethodAsync("initialize");

        var result = new JsonObject
        {
            ["capabilities"] = capabilities
        };
        await RespondAsync(initializeRequest, result);
        await ReadMethodAsync("initialized");

        return initializeRequest;
    }

    /// <summary>
    /// Ends the server's process, the way a crash or a clean exit does.
    /// </summary>
    public void Exit()
    {
        _serverToClient.Writer.Complete();
        _clientToServer.Reader.Complete();
        _exited.TrySetResult();
    }

    public void Kill()
    {
        WasKilled = true;
        Exit();
    }

    public void Dispose()
    {
        WasDisposed = true;
    }
}
