using Celbridge.Console;
using Celbridge.Logging;

namespace Celbridge.Python.Services;

/// <summary>
/// The python session type: injects a celbridge-py command into the session's shell, starting an IPython
/// REPL that dials the shared cel-proxy JSON-RPC server and exposes cel.* against the workspace.
/// </summary>
public sealed class PythonSessionProvider : IConsoleSessionProvider
{
    // Carries the console's startup script to celbridge-py, which runs it as IPython exec_lines.
    private const string StartupScriptVariable = "CELBRIDGE_PYTHON_STARTUP";

    private readonly IPythonConfigService _pythonConfigService;
    private readonly IPythonLaunchService _launchService;
    private readonly ILogger<PythonSessionProvider> _logger;

    public PythonSessionProvider(
        IPythonConfigService pythonConfigService,
        IPythonLaunchService launchService,
        ILogger<PythonSessionProvider> logger)
    {
        _pythonConfigService = pythonConfigService;
        _launchService = launchService;
        _logger = logger;
    }

    public string TypeId => "python";

    public IReadOnlyList<ConsoleRunner> DefaultRunners { get; } = new[]
    {
        new ConsoleRunner(new[] { ".py", ".ipy" }, "%run \"{resource}\""),
    };

    public async Task<Result<ConsoleStartupInvocation>> BuildStartupInvocationAsync(ConsoleSessionContext context)
    {
        var pythonVersion = ResolvePythonVersion(context);
        var dependencies = context.Dependencies ?? Array.Empty<string>();

        // A python console has no executable to pass arguments to, and raw interpreter flags are not part
        // of its configuration surface, so context.Arguments is deliberately unused here. The REPL is
        // configured through the startup script, which runs as IPython exec_lines.
        var request = new PythonLaunchRequest(
            context.ProjectFolderPath,
            pythonVersion,
            dependencies);

        var startupResult = await _launchService.BuildStartupAsync(request);
        if (startupResult.IsFailure)
        {
            return Result<ConsoleStartupInvocation>.Fail("Failed to build the Python startup invocation")
                .WithErrors(startupResult);
        }
        var startup = startupResult.Value;

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
}
