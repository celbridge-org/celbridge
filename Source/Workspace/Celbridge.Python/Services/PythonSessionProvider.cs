using System.Collections.Concurrent;
using Celbridge.Console;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Projects;

namespace Celbridge.Python.Services;

/// <summary>
/// The python session type: an IPython REPL that dials the shared cel-proxy JSON-RPC server and exposes
/// cel.* against the workspace. All the Python-specific launch machinery lives behind PythonLaunchService,
/// so no Python knowledge leaks into the host.
/// </summary>
public sealed class PythonSessionProvider : IConsoleSessionProvider, IDisposable
{
    private readonly IProjectService _projectService;
    private readonly IPythonConfigService _pythonConfigService;
    private readonly IPythonLaunchService _launchService;
    private readonly IMessengerService _messengerService;
    private readonly ILogger<PythonSessionProvider> _logger;

    // Pending offline-mode fingerprints keyed by session token, saved once the session's client connects
    // back (Ready), so the fingerprint is only trusted after the config has proven it launches.
    private readonly ConcurrentDictionary<Guid, PendingFingerprint> _pendingFingerprints = new();

    private sealed record PendingFingerprint(string Fingerprint, string ProjectPythonFolder);

    public PythonSessionProvider(
        IProjectService projectService,
        IPythonConfigService pythonConfigService,
        IPythonLaunchService launchService,
        IMessengerService messengerService,
        ILogger<PythonSessionProvider> logger)
    {
        _projectService = projectService;
        _pythonConfigService = pythonConfigService;
        _launchService = launchService;
        _messengerService = messengerService;
        _logger = logger;

        _messengerService.Register<ConsoleSessionStateChangedMessage>(this, OnConsoleSessionStateChanged);
    }

    public string TypeId => "python";

    public ConsoleHostBinding HostBinding => ConsoleHostBinding.CelProxy;

    public IReadOnlyList<ConsoleRunner> DefaultRunners { get; } = new[]
    {
        new ConsoleRunner(new[] { ".py", ".ipy" }, "%run \"{script_path}\""),
    };

    public async Task<Result<ConsoleLaunchSpec>> BuildLaunchSpecAsync(ConsoleSessionContext context)
    {
        var pythonVersion = ResolvePythonVersion(context);
        var dependencies = context.Dependencies ?? Array.Empty<string>();

        var request = new PythonLaunchRequest(
            context.ProjectFolderPath,
            pythonVersion,
            dependencies,
            context.Arguments,
            context.Environment);

        var launchResult = await _launchService.BuildLaunchAsync(request);
        if (launchResult.IsFailure)
        {
            return Result<ConsoleLaunchSpec>.Fail("Failed to build the Python launch")
                .WithErrors(launchResult);
        }
        var launch = launchResult.Value;

        // Remember the fingerprint so it can be persisted once the session connects back. The token was
        // seeded into the environment by the console channel.
        if (context.Environment.TryGetValue("CELBRIDGE_SESSION_TOKEN", out var tokenText)
            && Guid.TryParse(tokenText, out var sessionToken))
        {
            _pendingFingerprints[sessionToken] = new PendingFingerprint(launch.Fingerprint, launch.ProjectPythonFolder);
        }

        var workingDirectory = ResolveWorkingDirectory(context.WorkingDirectory, context.ProjectFolderPath);
        var launchSpec = new ConsoleLaunchSpec(launch.CommandLine, workingDirectory, launch.Environment);
        return launchSpec;
    }

    // The version comes from the .console config first, then the project's requires-python, then the
    // bundled default, so a blank console field resolves to a sensible version.
    private string ResolvePythonVersion(ConsoleSessionContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.RuntimeVersion))
        {
            return context.RuntimeVersion;
        }

        var configuredVersion = _projectService.CurrentProject?.Config.Project.RequiresPython;
        if (!string.IsNullOrWhiteSpace(configuredVersion))
        {
            return configuredVersion;
        }

        return _pythonConfigService.DefaultPythonVersion;
    }

    private void OnConsoleSessionStateChanged(object recipient, ConsoleSessionStateChangedMessage message)
    {
        if (message.State == ConsoleSessionState.Ready)
        {
            if (_pendingFingerprints.TryRemove(message.SessionId, out var pending))
            {
                _ = _launchService.SaveFingerprintAsync(pending.ProjectPythonFolder, pending.Fingerprint);
            }
            return;
        }

        // A session that ended without ever connecting leaves its fingerprint unsaved, so the next run
        // reconciles online rather than trusting an unproven config.
        if (message.State == ConsoleSessionState.Disconnected)
        {
            _pendingFingerprints.TryRemove(message.SessionId, out _);
        }
    }

    // A relative working folder resolves against the project root, an absolute path is used as given, and a
    // blank value defaults to the project root.
    private static string ResolveWorkingDirectory(string workingDirectory, string projectFolderPath)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return projectFolderPath;
        }

        if (Path.IsPathRooted(workingDirectory))
        {
            return workingDirectory;
        }

        var combined = Path.Combine(projectFolderPath, workingDirectory);
        return Path.GetFullPath(combined);
    }

    public void Dispose()
    {
        _messengerService.UnregisterAll(this);
    }
}
