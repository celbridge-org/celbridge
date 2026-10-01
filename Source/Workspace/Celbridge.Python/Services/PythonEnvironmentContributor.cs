using Celbridge.Console;
using Celbridge.Logging;
using Celbridge.Utilities;

namespace Celbridge.Python.Services;

/// <summary>
/// Adds the shared Python environment to every console, and puts the uv bin folders on its PATH. The installed
/// celbridge-py command can then start a connected REPL from any console, or from a terminal a console starts.
/// Also removes the uv and Python settings Celbridge inherited.
/// </summary>
public sealed class PythonEnvironmentContributor : IConsoleEnvironmentContributor
{
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

        // Merge, so a PATH set by the console keeps its entries and gains the uv bin folders. After the user's
        // profile runs, the start-up moves these folders back to the front.
        var pathKey = ResolvePathKey(environment);
        environment.TryGetValue(pathKey, out var basePath);
        environment[pathKey] = _launchService.BuildConsolePath(basePath ?? string.Empty);
        environment[ConsoleEnvironmentVariables.PathFolders] = string.Join(
            Path.PathSeparator,
            _launchService.GetConsolePathFolders());
    }

    // Windows environment names are case-insensitive and its canonical spelling is Path, so a console that
    // wrote the name in another case has that key extended in place.
    private static string ResolvePathKey(IDictionary<string, string> environment)
    {
        foreach (var key in environment.Keys)
        {
            if (EnvironmentVariableNames.IsPath(key))
            {
                return key;
            }
        }

        return EnvironmentVariableNames.PathName;
    }
}
