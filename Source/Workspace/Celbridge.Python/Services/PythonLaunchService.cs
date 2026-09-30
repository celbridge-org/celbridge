using System.Diagnostics;
using Celbridge.FileSystem;
using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.Projects;
using Celbridge.Server;
using Celbridge.Utilities;

namespace Celbridge.Python.Services;

/// <summary>
/// The inputs to build a Python session's startup command: the project root, the interpreter version, and
/// the extra package dependencies.
/// </summary>
public sealed record PythonLaunchRequest(
    string PythonVersion,
    IReadOnlyList<string> Dependencies);

/// <summary>
/// The resolved startup: the installed celbridge-py tool to run, and the per-console environment
/// carrying its launch defaults.
/// </summary>
public sealed record PythonStartupResult(
    string Executable,
    IReadOnlyDictionary<string, string> Environment);

/// <summary>
/// Builds the startup command and shared environment for Python sessions, owning all the Python-specific
/// launch machinery. The console's command is the installed celbridge-py by its full path, so nothing on the
/// shell's PATH can stand in for it. The console's interpreter version and dependencies ride per-console
/// environment variables that the tool reads as launch defaults, so retyping celbridge-py after exiting the
/// REPL reproduces the same environment. The uv and wheel locations ride the shared console environment.
/// </summary>
public interface IPythonLaunchService
{
    /// <summary>
    /// Resolves the startup command and its per-console environment for a Python session, installing the
    /// support files first. Fails if the install fails or if uv is missing.
    /// </summary>
    Task<Result<PythonStartupResult>> BuildStartupAsync(PythonLaunchRequest request);

    /// <summary>
    /// The folders a console puts at the front of PATH, in order: the app's uv and tool bin folders, then the
    /// project's own uv tool bin folder, so uv, uvx, celbridge-py and any tool the user installs in the
    /// project resolve in a console.
    /// </summary>
    IReadOnlyList<string> GetConsolePathFolders();

    /// <summary>
    /// Returns a PATH value with the console's folders moved to the front of the given base.
    /// </summary>
    string BuildConsolePath(string basePath);

    /// <summary>
    /// Returns the host-integration environment every console shares, installing the Python support files
    /// first. A celbridge-py launched from any console then behaves like a python console session.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> BuildConsoleEnvironmentAsync();
}

public sealed class PythonLaunchService : IPythonLaunchService
{
    private const string ProjectUvToolsFolderName = "uv_tools";
    private const string ProjectUvBinFolderName = "uv_bin";
    private const string IPythonProfileFolderName = "ipython";

    private readonly IAppEnvironment _environmentService;
    private readonly IServerService _serverService;
    private readonly IPythonInstaller _pythonInstaller;
    private readonly ILocalFileSystem _fileSystem;
    private readonly IProjectService _projectService;
    private readonly ILogger<PythonLaunchService> _logger;

    public PythonLaunchService(
        IAppEnvironment environmentService,
        IServerService serverService,
        IPythonInstaller pythonInstaller,
        ILocalFileSystem fileSystem,
        IProjectService projectService,
        ILogger<PythonLaunchService> logger)
    {
        _environmentService = environmentService;
        _serverService = serverService;
        _pythonInstaller = pythonInstaller;
        _fileSystem = fileSystem;
        _projectService = projectService;
        _logger = logger;
    }

    // What belongs to this project alone: its IPython profile, and the uv tools the user installs here.
    // Under the project data folder, so two configurations in one project folder each get their own.
    private string ProjectPythonFolder => Path.Combine(
        _projectService.CurrentProject!.ProjectDataFolderPath,
        ProjectConstants.PythonFolder);

    private string ProjectUvBinFolder => Path.Combine(ProjectPythonFolder, ProjectUvBinFolderName);

    private string ProjectUvToolsFolder => Path.Combine(ProjectPythonFolder, ProjectUvToolsFolderName);

    public async Task<Result<PythonStartupResult>> BuildStartupAsync(PythonLaunchRequest request)
    {
        var startupTimer = Stopwatch.StartNew();

        var environmentInfo = _environmentService.GetEnvironmentInfo();

        // The celbridge-py command this returns is published by that install, so a console starting while
        // it runs waits for it here rather than launching against a command that is not there yet.
        var installResult = await _pythonInstaller.InstallPythonAsync(environmentInfo.AppVersion);
        if (installResult.IsFailure)
        {
            return Result<PythonStartupResult>.Fail("Failed to ensure Python support files are installed")
                .WithErrors(installResult);
        }

        var resolveUvResult = await ResolveUvExecutableAsync();
        if (resolveUvResult.IsFailure)
        {
            return Result<PythonStartupResult>.Fail("Failed to resolve uv for the Python console")
                .WithErrors(resolveUvResult);
        }

        // Filter blank entries, so a stray blank line cannot reach uv as an empty package specifier.
        var dependencies = request.Dependencies
            .Where(dependency => !string.IsNullOrWhiteSpace(dependency))
            .ToList();

        // These per-console variables are the launch defaults the console's celbridge-py reads, making it
        // re-exec through uv (located via the shared console environment) with this console's interpreter and
        // packages. Dependencies are newline-separated because PEP 508 specifiers can contain commas and
        // semicolons. Offline mode is not among them: celbridge-py measures the cache itself at launch.
        var startupEnvironment = new Dictionary<string, string>
        {
            ["CELBRIDGE_PYTHON_VERSION"] = request.PythonVersion,
        };

        if (dependencies.Count > 0)
        {
            startupEnvironment["CELBRIDGE_PYTHON_WITH"] = string.Join('\n', dependencies);
        }

        var celbridgeToolCommand = _pythonInstaller.CelbridgeToolCommandPath;

        _logger.LogDebug("Built Python startup in {DurationMs}ms: {Command} with launch defaults {Environment}",
            startupTimer.ElapsedMilliseconds,
            celbridgeToolCommand,
            string.Join(' ', startupEnvironment.Select(pair => $"{pair.Key}={pair.Value.Replace('\n', ';')}")));

        var result = new PythonStartupResult(celbridgeToolCommand, startupEnvironment);
        return result;
    }

    // The app's folders come first, so they outrank the project's. A project can hold a celbridge-py of its
    // own, and a stale shim ahead of the installed one would be found first.
    public IReadOnlyList<string> GetConsolePathFolders()
    {
        var folders = new List<string>
        {
            _pythonInstaller.UvBinFolderPath,
            _pythonInstaller.UvToolBinFolderPath,
            ProjectUvBinFolder,
        };

        return folders;
    }

    // Each folder is moved to the front even when the base already carried it.
    public string BuildConsolePath(string basePath)
    {
        var consolePath = basePath;
        foreach (var folder in GetConsolePathFolders().Reverse())
        {
            consolePath = PrependPathFolder(consolePath, folder);
        }

        return consolePath;
    }

    private string PrependPathFolder(string path, string folder)
    {
        if (PathListHelper.TryPrepend(path, folder, out var consolePath))
        {
            return consolePath;
        }

        _logger.LogWarning(
            "Folder '{Folder}' was left off the console PATH because it contains the path separator '{Separator}'",
            folder,
            Path.PathSeparator);

        return consolePath;
    }

    public async Task<IReadOnlyDictionary<string, string>> BuildConsoleEnvironmentAsync()
    {
        var environmentInfo = _environmentService.GetEnvironmentInfo();

        // Every console type advertises the uv bin folder on its PATH, so the install has to have finished
        // before the environment is handed over. A reinstall empties that folder while it runs.
        var installResult = await _pythonInstaller.InstallPythonAsync(environmentInfo.AppVersion);
        if (installResult.IsFailure)
        {
            _logger.LogError(
                "Failed to install Python support files, so the console environment omits uv: {Error}",
                installResult.FirstErrorMessage);
        }

        var ipythonDir = Path.Combine(ProjectPythonFolder, IPythonProfileFolderName);
        await _fileSystem.CreateFolderAsync(ipythonDir);

        var celbridgeVersion = environmentInfo.Configuration == "Debug"
            ? $"{environmentInfo.AppVersion} (Debug)"
            : $"{environmentInfo.AppVersion}";

        var uvCacheDir = _pythonInstaller.UvCacheFolderPath;

        // uv's own variables, so a uv the user types in a console works against the same folders the
        // console's own Python does. A console can override any of them. Sharing the cache does not share
        // imports: each REPL still gets its own environment from the inner uv run.
        var environment = new Dictionary<string, string>
        {
            ["UV_CACHE_DIR"] = uvCacheDir,
            ["UV_PYTHON_INSTALL_DIR"] = _pythonInstaller.UvPythonInstallFolderPath,
            ["UV_TOOL_DIR"] = ProjectUvToolsFolder,
            ["UV_TOOL_BIN_DIR"] = ProjectUvBinFolder,

            // Where a typed uv python install links the interpreters it installs, which uv otherwise puts in
            // the user's own ~/.local/bin.
            ["UV_PYTHON_BIN_DIR"] = ProjectUvBinFolder,

            // A bare uv venv downloads the interpreter it needs into the project. Left to uv's default it
            // would take whatever Python the host happens to carry, which on macOS is Xcode's 3.9. This is
            // the variable behind --managed-python, which the REPL's own launch passes: the older
            // UV_PYTHON_PREFERENCE names a different argument, and uv rejects the pair.
            ["UV_MANAGED_PYTHON"] = "1",
            ["CELBRIDGE_MCP_PORT"] = _serverService.Port.ToString(),
            ["CELBRIDGE_PROJECT_FOLDER"] = _projectService.CurrentProject!.ProjectFolderPath,
            ["CELBRIDGE_VERSION"] = celbridgeVersion,
            ["CELBRIDGE_IPYTHON_DIR"] = ipythonDir,

            // Read by celbridge-py, which passes it as an explicit --cache-dir. That outranks any
            // UV_CACHE_DIR a console or a shell profile sets, holding the REPL to the warmed cache.
            ["CELBRIDGE_UV_CACHE_DIR"] = uvCacheDir,
        };

        // Where a typed celbridge-py finds uv and the celbridge wheel when its launch options make it
        // re-exec through uv. Only set once the support files are installed, because before that no
        // celbridge-py exists to consume them.
        var resolveUvResult = await ResolveUvExecutableAsync();
        if (resolveUvResult.IsSuccess)
        {
            environment["CELBRIDGE_UV"] = resolveUvResult.Value;
        }

        var wheelPathResult = await _pythonInstaller.GetInstalledWheelPathAsync();
        if (wheelPathResult.IsSuccess)
        {
            environment["CELBRIDGE_WHEEL"] = wheelPathResult.Value;
        }

        return environment;
    }

    // The install reports success before this is called, so a missing binary means something removed it
    // afterwards rather than an install that has not run.
    private async Task<Result<string>> ResolveUvExecutableAsync()
    {
        var uvExePath = _pythonInstaller.UvExecutablePath;

        var uvExeInfoResult = await _fileSystem.GetInfoAsync(uvExePath);
        var uvExeExists = uvExeInfoResult.IsSuccess
            && uvExeInfoResult.Value.Kind == StorageItemKind.File;
        if (!uvExeExists)
        {
            return Result<string>.Fail($"uv not found at '{uvExePath}'");
        }

        return Result<string>.Ok(uvExePath);
    }
}
