using System.Net;
using System.Net.Sockets;
using Celbridge.Console;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Server;
using Celbridge.Workspace;

namespace Celbridge.Python.Services;

/// <summary>
/// The legacy load-time Python REPL host: it owns a JSON-RPC listener and drives the single bottom-panel
/// terminal with a Python launch built by the shared PythonLaunchService. The per-document console path
/// uses the session providers instead; this service is removed when the load-time REPL is dropped.
/// </summary>
public class PythonService : IPythonService, IDisposable
{
    private readonly IProjectService _projectService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IMessengerService _messengerService;
    private readonly IPythonLaunchService _launchService;
    private readonly ILogger<PythonService> _logger;
    private readonly ITcpTransport _tcpTransport;
    private CancellationTokenSource? _rpcCancellationTokenSource;
    private string _pendingFingerprint = string.Empty;
    private string _pendingProjectPythonFolder = string.Empty;
    private bool _fingerprintSaved;
    private volatile bool _hadConnection;

    public PythonService(
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper,
        IMessengerService messengerService,
        IPythonLaunchService launchService,
        ILogger<PythonService> logger,
        ITcpTransport tcpTransport)
    {
        _projectService = projectService;
        _workspaceWrapper = workspaceWrapper;
        _messengerService = messengerService;
        _launchService = launchService;
        _logger = logger;
        _tcpTransport = tcpTransport;
    }

    public bool IsPythonHostAvailable { get; private set; } = false;

    public async Task<Result> InitializePython()
    {
        try
        {
            var project = _projectService.CurrentProject;
            if (project is null)
            {
                return Result.Fail("Failed to run python as no project is loaded");
            }

            var projectFileName = Path.GetFileName(project.ProjectFilePath);

            var pythonConfig = project.Config.Project;
            var pythonVersion = pythonConfig.RequiresPython;
            if (string.IsNullOrWhiteSpace(pythonVersion))
            {
                var errorMessage = new ConsoleErrorMessage(ConsoleErrorType.InvalidProjectConfig, projectFileName);
                _messengerService.Send(errorMessage);
                return Result.Fail($"Python version not specified in requires-python field in project config '{projectFileName}'");
            }

            // The legacy REPL owns its own listener on a dynamically chosen port, independent of the shared
            // cel-proxy listener the per-document consoles use.
            var rpcPort = GetAvailableTcpPort();
            _logger.LogInformation("Selected RPC TCP port: {Port}", rpcPort);

            var extraEnvironment = new Dictionary<string, string>
            {
                ["CELBRIDGE_RPC_PORT"] = rpcPort.ToString(),
            };

            var dependencies = pythonConfig.Dependencies ?? Array.Empty<string>();

            var launchRequest = new PythonLaunchRequest(
                project.ProjectFolderPath,
                pythonVersion,
                dependencies,
                Array.Empty<string>(),
                extraEnvironment);

            var launchResult = await _launchService.BuildLaunchAsync(launchRequest);
            if (launchResult.IsFailure)
            {
                var errorMessage = new ConsoleErrorMessage(ConsoleErrorType.PythonHostPreInitError, projectFileName);
                _messengerService.Send(errorMessage);
                return Result.Fail("Failed to build the Python launch")
                    .WithErrors(launchResult);
            }
            var launch = launchResult.Value;

            // Keep the terminal alive after the REPL exits so the user can start a new session by hand. The
            // Unix backend already runs the command through /bin/sh -c, so append 'exec $SHELL' rather than
            // wrapping in a second shell that would collide with the command's own quoting.
            var commandLine = OperatingSystem.IsWindows()
                ? $"cmd.exe /k \"{launch.CommandLine}\""
                : $"{launch.CommandLine}; exec $SHELL";

            // Cancel any previous RPC listening loop in case InitializePython is called again after reload.
            _rpcCancellationTokenSource?.Cancel();
            _rpcCancellationTokenSource?.Dispose();

            _tcpTransport.ConnectionAccepted -= OnConnectionAccepted;
            _tcpTransport.ConnectionLost -= OnConnectionLost;

            _pendingFingerprint = launch.Fingerprint;
            _pendingProjectPythonFolder = launch.ProjectPythonFolder;
            _fingerprintSaved = false;
            _hadConnection = false;

            _tcpTransport.ConnectionAccepted += OnConnectionAccepted;
            _tcpTransport.ConnectionLost += OnConnectionLost;

            _rpcCancellationTokenSource = new CancellationTokenSource();
            _ = Task.Run(() => _tcpTransport.StartListeningAsync(rpcPort, _rpcCancellationTokenSource.Token));

            var terminal = _workspaceWrapper.WorkspaceService.ConsoleService.Terminal;

            terminal.ProcessExited += (sender, eventArgs) =>
            {
                if (!_hadConnection)
                {
                    _logger.LogError("Python process exited before establishing an RPC connection");
                    var projectFile = Path.GetFileName(project.ProjectFilePath);
                    var errorMessage = new ConsoleErrorMessage(ConsoleErrorType.PythonHostProcessError, projectFile);
                    _messengerService.Send(errorMessage);
                }
            };

            var environment = new Dictionary<string, string>(launch.Environment);
            terminal.Start(commandLine, project.ProjectFolderPath, environment);
            _logger.LogInformation("Python terminal started successfully");

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("An error occurred when initializing Python")
                         .WithException(ex);
        }
    }

    private void OnConnectionAccepted(int connectionId)
    {
        _logger.LogInformation("Python RPC connection {ConnectionId} established", connectionId);
        _hadConnection = true;

        // Save the fingerprint on the first successful connection so subsequent runs can use offline mode.
        // Block on the async save here because this event handler is sync and the operation is small.
        if (!_fingerprintSaved)
        {
            _launchService.SaveFingerprintAsync(_pendingProjectPythonFolder, _pendingFingerprint).GetAwaiter().GetResult();
            _fingerprintSaved = true;
        }

        IsPythonHostAvailable = true;
        _messengerService.Send(new PythonHostInitializedMessage());
    }

    private void OnConnectionLost(int connectionId)
    {
        _logger.LogInformation("Python RPC connection {ConnectionId} lost", connectionId);
        IsPythonHostAvailable = _tcpTransport.ActiveConnectionCount > 0;
    }

    private static int GetAvailableTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private bool _disposed;

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
                _rpcCancellationTokenSource?.Cancel();
                _rpcCancellationTokenSource?.Dispose();
                _rpcCancellationTokenSource = null;

                _tcpTransport.Dispose();
            }

            _disposed = true;
        }
    }

    ~PythonService()
    {
        Dispose(false);
    }
}
