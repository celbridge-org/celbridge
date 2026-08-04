using Celbridge.WebHost;
using Celbridge.Workspace;

namespace Celbridge.Console.Services;

/// <summary>
/// The console document's channel: a thin adapter between the WebView and the live session. The session
/// runs in the workspace-scoped session service whether or not a view exists; this channel attaches to it
/// when the WebView initializes, replays its buffered output, and forwards I/O while attached.
/// </summary>
internal sealed class ConsoleSessionChannel : ICustomEditorChannel, IConsoleSessionRpc, IConsoleView
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ResourceKey _fileResource;

    private ICustomEditorChannelHost? _host;
    private bool _disposed;

    public ConsoleSessionChannel(
        IServiceProvider serviceProvider,
        ResourceKey fileResource)
    {
        _fileResource = fileResource;
        _workspaceWrapper = serviceProvider.GetRequiredService<IWorkspaceWrapper>();
    }

    private IConsoleSessionService Sessions => _workspaceWrapper.WorkspaceService.ConsoleService.Sessions;

    public void RegisterTargets(ICustomEditorChannelHost host)
    {
        _host = host;
        host.AddLocalRpcTarget<IConsoleSessionRpc>(this);
    }

    public async Task<ConsoleAttachResult> AttachAsync(int cols, int rows)
    {
        var snapshot = await Sessions.AttachAsync(_fileResource, this, cols, rows);
        return ToResult(snapshot, Sessions.GetBuiltInRunners());
    }

    public async Task<ConsoleAttachResult> ReopenAsync(int cols, int rows)
    {
        var snapshot = await Sessions.ReopenAsync(_fileResource, cols, rows);
        return ToResult(snapshot, Sessions.GetBuiltInRunners());
    }

    public void OnInput(string data)
    {
        Sessions.Input(_fileResource, data);
    }

    public void OnSubmit(string invocation)
    {
        Sessions.SubmitInvocation(_fileResource, invocation);
    }

    public void OnResize(int cols, int rows)
    {
        Sessions.Resize(_fileResource, cols, rows);
    }

    public void OnOutput(string text)
    {
        if (_disposed)
        {
            return;
        }

        // The pty read loop is a single background thread, so notifications stay ordered and StreamJsonRpc
        // serializes the writes. The adapter no-ops if the host has been torn down.
        _ = _host?.NotifyAsync(ConsoleSessionRpcMethods.Write, new { text });
    }

    public void OnSessionEnded()
    {
        if (_disposed)
        {
            return;
        }

        _ = _host?.NotifyAsync(ConsoleSessionRpcMethods.SessionState, new { state = ConsoleSessionStates.Ended });
    }

    public void OnStartupComplete()
    {
        if (_disposed)
        {
            return;
        }

        _ = _host?.NotifyAsync(ConsoleSessionRpcMethods.StartupComplete, new { });
    }

    private static ConsoleAttachResult ToResult(
        ConsoleAttachSnapshot snapshot,
        IReadOnlyDictionary<string, IReadOnlyList<ConsoleRunner>> builtInRunners)
    {
        // An unrecognized state throws rather than falling back to failed, so a state added to
        // ConsoleSessionRunState without a wire spelling surfaces instead of reading as a broken session.
        var state = snapshot.State switch
        {
            ConsoleSessionRunState.Starting => ConsoleSessionStates.Starting,
            ConsoleSessionRunState.Running => ConsoleSessionStates.Running,
            ConsoleSessionRunState.Ended => ConsoleSessionStates.Ended,
            ConsoleSessionRunState.Failed => ConsoleSessionStates.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(snapshot), snapshot.State, "Unhandled console session run state"),
        };

        return new ConsoleAttachResult(
            state,
            snapshot.Error,
            snapshot.StartupPending,
            snapshot.Replay,
            snapshot.LaunchedConfigToml,
            builtInRunners);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_workspaceWrapper.HasWorkspaceService)
        {
            Sessions.Detach(_fileResource, this);
        }

        _host = null;
    }
}
