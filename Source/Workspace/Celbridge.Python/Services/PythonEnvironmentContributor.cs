using Celbridge.Console;

namespace Celbridge.Python.Services;

/// <summary>
/// Gives every console the shared Python host-integration environment (host ports, tool feature flags,
/// version, per-project folders) and puts the project's uv tool bin folder on its PATH, so the installed
/// celbridge-py command starts a fully-featured cel-connected REPL from any console type or a terminal a
/// console spawns.
/// </summary>
public sealed class PythonEnvironmentContributor : IConsoleEnvironmentContributor
{
    private readonly IPythonLaunchService _launchService;

    public PythonEnvironmentContributor(IPythonLaunchService launchService)
    {
        _launchService = launchService;
    }

    public async Task ContributeAsync(ConsoleSessionContext context, IDictionary<string, string> environment)
    {
        var hostEnvironment = await _launchService.BuildConsoleEnvironmentAsync(context.ProjectFolderPath);

        foreach (var pair in hostEnvironment)
        {
            // PATH merges rather than replaces, so a console-configured PATH still gains uv_bin.
            if (pair.Key == "PATH")
            {
                environment.TryGetValue("PATH", out var basePath);
                environment["PATH"] = _launchService.BuildConsolePath(context.ProjectFolderPath, basePath);
                continue;
            }

            // A variable the provider or the console's own [session.environment] already set wins.
            if (!environment.ContainsKey(pair.Key))
            {
                environment[pair.Key] = pair.Value;
            }
        }
    }
}
