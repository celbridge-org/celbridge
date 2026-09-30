using Celbridge.Console;
using Celbridge.Logging;

namespace Celbridge.Python.Services;

/// <summary>
/// Gives every console the shared Python host-integration environment (host ports, tool feature flags,
/// version, per-project folders) and puts the uv bin folders on its PATH, so the installed celbridge-py
/// command starts a fully-featured cel-connected REPL from any console type or a terminal a console
/// spawns. Keeps the uv and Python settings the application inherited out of every console.
/// </summary>
public sealed class PythonEnvironmentContributor : IConsoleEnvironmentContributor
{
    private const string PathVariableName = "PATH";

    private readonly IPythonLaunchService _launchService;
    private readonly ILogger<PythonEnvironmentContributor> _logger;

    public PythonEnvironmentContributor(
        IPythonLaunchService launchService,
        ILogger<PythonEnvironmentContributor> logger)
    {
        _launchService = launchService;
        _logger = logger;
    }

    public void FilterInheritedEnvironment(IDictionary<string, string> inheritedEnvironment)
    {
        var removedNames = PythonEnvironmentFilter.Apply(inheritedEnvironment);
        if (removedNames.Count > 0)
        {
            _logger.LogDebug(
                "Left the inherited uv and Python variables out of the console: {Names}",
                string.Join(", ", removedNames));
        }
    }

    public async Task ContributeAsync(ConsoleSessionContext context, IDictionary<string, string> environment)
    {
        var hostEnvironment = await _launchService.BuildConsoleEnvironmentAsync();

        foreach (var pair in hostEnvironment)
        {
            // A variable the provider or the console's own [session.environment] already set wins.
            if (!environment.ContainsKey(pair.Key))
            {
                environment[pair.Key] = pair.Value;
            }
        }

        // A merge, so a console-configured PATH keeps its entries and gains the uv bin folders.
        var pathKey = ResolvePathKey(environment);
        environment.TryGetValue(pathKey, out var basePath);
        environment[pathKey] = _launchService.BuildConsolePath(basePath);
    }

    // Windows environment names are case-insensitive and its canonical spelling is Path, so a console that
    // wrote the name in another case has that key extended in place.
    private static string ResolvePathKey(IDictionary<string, string> environment)
    {
        foreach (var key in environment.Keys)
        {
            if (string.Equals(key, PathVariableName, StringComparison.OrdinalIgnoreCase))
            {
                return key;
            }
        }

        return PathVariableName;
    }
}
