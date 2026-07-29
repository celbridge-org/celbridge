using System.Collections.Concurrent;
using Celbridge.Console;
using Celbridge.Logging;
using Celbridge.Messaging;

namespace Celbridge.Python.Services;

/// <summary>
/// The python session type: injects a celbridge-py command into the session's shell, starting an IPython
/// REPL that dials the shared cel-proxy JSON-RPC server and exposes cel.* against the workspace.
/// </summary>
public sealed class PythonSessionProvider : IConsoleSessionProvider, IDisposable
{
    // Carries the console's startup script to celbridge-py, which runs it as IPython exec_lines.
    private const string StartupScriptVariable = "CELBRIDGE_PYTHON_STARTUP";

    private readonly IPythonConfigService _pythonConfigService;
    private readonly IPythonLaunchService _launchService;
    private readonly IMessengerService _messengerService;
    private readonly ILogger<PythonSessionProvider> _logger;

    // Pending offline-mode fingerprints keyed by session token, saved once the session's client connects
    // back (Ready), so the fingerprint is only trusted after the config has proven it launches.
    private readonly ConcurrentDictionary<Guid, PendingFingerprint> _pendingFingerprints = new();

    private sealed record PendingFingerprint(string Fingerprint, string ProjectPythonFolder);

    public PythonSessionProvider(
        IPythonConfigService pythonConfigService,
        IPythonLaunchService launchService,
        IMessengerService messengerService,
        ILogger<PythonSessionProvider> logger)
    {
        _pythonConfigService = pythonConfigService;
        _launchService = launchService;
        _messengerService = messengerService;
        _logger = logger;

        _messengerService.Register<ConsoleSessionConnectedMessage>(this, OnConsoleSessionConnected);
        _messengerService.Register<ConsoleSessionStateChangedMessage>(this, OnConsoleSessionStateChanged);
    }

    public string TypeId => "python";

    public IReadOnlyList<ConsoleRunner> DefaultRunners { get; } = new[]
    {
        new ConsoleRunner(new[] { ".py", ".ipy" }, "%run \"{script_path}\""),
    };

    public async Task<Result<ConsoleStartupInvocation>> BuildStartupInvocationAsync(ConsoleSessionContext context)
    {
        var pythonVersion = ResolvePythonVersion(context);
        var dependencies = context.Dependencies ?? Array.Empty<string>();

        var request = new PythonLaunchRequest(
            context.ProjectFolderPath,
            pythonVersion,
            dependencies,
            context.Arguments);

        var startupResult = await _launchService.BuildStartupAsync(request);
        if (startupResult.IsFailure)
        {
            return Result<ConsoleStartupInvocation>.Fail("Failed to build the Python startup invocation")
                .WithErrors(startupResult);
        }
        var startup = startupResult.Value;

        // Remember the fingerprint so it can be persisted once the session connects back. The token was
        // seeded into the environment by the console channel.
        if (context.Environment.TryGetValue(ConsoleEnvironmentVariables.SessionToken, out var tokenText)
            && Guid.TryParse(tokenText, out var sessionToken))
        {
            _pendingFingerprints[sessionToken] = new PendingFingerprint(startup.Fingerprint, startup.ProjectPythonFolder);
        }

        // The startup script goes to IPython as exec_lines rather than being typed at the prompt: the REPL
        // discards pending terminal input as it starts, so injected type-ahead is lost.
        var environment = new Dictionary<string, string>(startup.Environment);
        var handlesStartupScript = !string.IsNullOrWhiteSpace(context.StartupScript);
        if (handlesStartupScript)
        {
            environment[StartupScriptVariable] = context.StartupScript!;
        }

        var startupInvocation = new ConsoleStartupInvocation(
            startup.Executable,
            Array.Empty<string>(),
            environment,
            handlesStartupScript);

        return startupInvocation;
    }

    // The version comes from the .console config, or the bundled default when the console field is blank.
    private string ResolvePythonVersion(ConsoleSessionContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.RuntimeVersion))
        {
            return context.RuntimeVersion;
        }

        return _pythonConfigService.DefaultPythonVersion;
    }

    // A connected client proves the config launches, so the fingerprint is persisted for offline mode.
    private void OnConsoleSessionConnected(object recipient, ConsoleSessionConnectedMessage message)
    {
        if (_pendingFingerprints.TryRemove(message.SessionId, out var pending))
        {
            _ = _launchService.SaveFingerprintAsync(pending.ProjectPythonFolder, pending.Fingerprint);
        }
    }

    // A session that ended without ever connecting leaves its fingerprint unsaved, so the next run
    // reconciles online rather than trusting an unproven config.
    private void OnConsoleSessionStateChanged(object recipient, ConsoleSessionStateChangedMessage message)
    {
        if (message.State == ConsoleSessionState.Ended)
        {
            _pendingFingerprints.TryRemove(message.SessionId, out _);
        }
    }

    public void Dispose()
    {
        _messengerService.UnregisterAll(this);
    }
}
