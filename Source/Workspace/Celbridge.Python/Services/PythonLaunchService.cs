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
/// The resolved startup: the installed celbridge-py tool to inject, and the per-console environment
/// carrying its launch defaults.
/// </summary>
public sealed record PythonStartupResult(
    string Executable,
    IReadOnlyDictionary<string, string> Environment);

/// <summary>
/// Builds the startup command and shared environment for Python sessions, owning all the Python-specific
/// launch machinery. The injected command is a bare celbridge-py; the console's interpreter version and
/// dependencies ride per-console environment variables that the tool reads as launch defaults, so retyping
/// celbridge-py after exiting the REPL reproduces the same environment. The uv and wheel locations ride the
/// shared console environment.
/// </summary>
public interface IPythonLaunchService
{
    /// <summary>
    /// Resolves the startup command and its per-console environment for a Python session, installing the
    /// support files first. Fails if the install fails or if uv is missing.
    /// </summary>
    Task<Result<PythonStartupResult>> BuildStartupAsync(PythonLaunchRequest request);

    /// <summary>
    /// Returns a PATH value with the app's uv and tool bin folders, and the project's own uv tool bin
    /// folder, prepended to the given base (or to the resolved child-process base PATH when null), so uv,
    /// uvx, celbridge-py and any tool the user installs in the project resolve in a console. A folder
    /// already on the given base keeps its position. An interactive shell sources its profile after this
    /// is applied and may prepend its own folders.
    /// </summary>
    string BuildConsolePath(string? basePath);

    /// <summary>
    /// Returns the host-integration environment every console shares, installing the Python support files
    /// first. A celbridge-py launched from any console then behaves like a python console session.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> BuildConsoleEnvironmentAsync();
}

public sealed class PythonLaunchService : IPythonLaunchService
{
    private const int LoginShellPathTimeoutMs = 5000;

    private const string CelbridgeToolCommand = "celbridge-py";
    private const string UVExecutableName = "uv";
    private const string UVExecutableNameWindows = "uv.exe";
    private const string UVToolsFolderName = "uv_tools";
    private const string UVBinFolderName = "uv_bin";
    private const string IPythonProfileFolderName = "ipython";

    private readonly IAppEnvironment _environmentService;
    private readonly IServerService _serverService;
    private readonly IPythonInstaller _pythonInstaller;
    private readonly ILocalFileSystem _fileSystem;
    private readonly IProjectService _projectService;
    private readonly ILogger<PythonLaunchService> _logger;

    // The login-shell PATH is app-global and costs a subprocess to resolve, so cache it for the app run.
    private static string? _resolvedLoginShellPath;
    private static readonly object _loginShellPathLock = new();

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

    private string ProjectUVBinFolder => Path.Combine(ProjectPythonFolder, UVBinFolderName);

    private string ProjectUVToolsFolder => Path.Combine(ProjectPythonFolder, UVToolsFolderName);

    private string UvExecutablePath => Path.Combine(
        _pythonInstaller.UvBinFolderPath,
        OperatingSystem.IsWindows() ? UVExecutableNameWindows : UVExecutableName);

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

        var uvExePath = UvExecutablePath;
        var uvExeInfoResult = await _fileSystem.GetInfoAsync(uvExePath);
        var uvExeExists = uvExeInfoResult.IsSuccess
            && uvExeInfoResult.Value.Kind == StorageItemKind.File;
        if (!uvExeExists)
        {
            return Result<PythonStartupResult>.Fail($"uv not found at '{uvExePath}'");
        }

        // Filter blank entries, so a stray blank line cannot reach uv as an empty package specifier.
        var dependencies = request.Dependencies
            .Where(dependency => !string.IsNullOrWhiteSpace(dependency))
            .ToList();

        // The injected command is a bare celbridge-py; these per-console variables are the launch
        // defaults it reads, making the tool re-exec through uv (located via the shared console
        // environment) with this console's interpreter and packages. Dependencies are newline-separated
        // because PEP 508 specifiers can contain commas and semicolons. Offline mode is not among them:
        // celbridge-py measures the cache itself at launch.
        var startupEnvironment = new Dictionary<string, string>
        {
            ["CELBRIDGE_PYTHON_VERSION"] = request.PythonVersion,
        };

        if (dependencies.Count > 0)
        {
            startupEnvironment["CELBRIDGE_PYTHON_WITH"] = string.Join('\n', dependencies);
        }

        _logger.LogDebug("Built Python startup in {DurationMs}ms: {Command} with launch defaults {Environment}",
            startupTimer.ElapsedMilliseconds,
            CelbridgeToolCommand,
            string.Join(' ', startupEnvironment.Select(pair => $"{pair.Key}={pair.Value.Replace('\n', ';')}")));

        var result = new PythonStartupResult(CelbridgeToolCommand, startupEnvironment);
        return result;
    }

    public string BuildConsolePath(string? basePath)
    {
        var resolvedBase = string.IsNullOrEmpty(basePath) ? ResolveChildProcessBasePath() : basePath;

        // The app's folders are prepended last, so they outrank the project's. A project can hold a
        // celbridge-py of its own, and a stale shim ahead of the installed one would be found first.
        var consolePath = PrependPathFolder(resolvedBase, ProjectUVBinFolder);
        consolePath = PrependPathFolder(consolePath, _pythonInstaller.UvToolBinFolderPath);

        return PrependPathFolder(consolePath, _pythonInstaller.UvBinFolderPath);
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
        // console's own Python does. A console can override any of them. The cache and the interpreter
        // store are the application's, shared by every project; each REPL still gets its own environment
        // from the inner uv run, so what a project imports is its own. The tool folders are the project's,
        // and hold whatever the user installs here.
        var environment = new Dictionary<string, string>
        {
            ["UV_CACHE_DIR"] = uvCacheDir,
            ["UV_PYTHON_INSTALL_DIR"] = _pythonInstaller.UvPythonInstallFolderPath,
            ["UV_TOOL_DIR"] = ProjectUVToolsFolder,
            ["UV_TOOL_BIN_DIR"] = ProjectUVBinFolder,

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

        // The bootstrapper variables: where a typed celbridge-py finds uv and the celbridge wheel when its
        // launch options make it re-exec through uv. Only set once the support files are installed; before
        // that no celbridge-py tool exists to consume them.
        var uvExePath = UvExecutablePath;
        var uvExeInfoResult = await _fileSystem.GetInfoAsync(uvExePath);
        var uvExeExists = uvExeInfoResult.IsSuccess
            && uvExeInfoResult.Value.Kind == StorageItemKind.File;
        if (uvExeExists)
        {
            environment["CELBRIDGE_UV"] = uvExePath;
        }

        var findWheelResult = await FindWheelFileAsync(_pythonInstaller.PythonFolderPath, "celbridge");
        if (findWheelResult.IsSuccess)
        {
            environment["CELBRIDGE_WHEEL"] = findWheelResult.Value;
        }

        return environment;
    }

    private async Task<Result<string>> FindWheelFileAsync(string folderPath, string packageName)
    {
        var searchPattern = $"{packageName}-*.whl";
        var enumerateFilesResult = await _fileSystem.EnumerateAsync(folderPath, searchPattern, recursive: false);
        if (enumerateFilesResult.IsFailure)
        {
            return Result<string>.Fail($"Error searching for wheel files for package '{packageName}'")
                .WithErrors(enumerateFilesResult);
        }

        var wheelFiles = enumerateFilesResult.Value
            .Where(entry => !entry.IsFolder)
            .Select(entry => entry.FullPath)
            .ToList();
        if (wheelFiles.Count == 0)
        {
            return Result<string>.Fail($"No wheel files found for package '{packageName}' in '{folderPath}'");
        }

        return Result<string>.Ok(wheelFiles[0]);
    }

    // The base PATH for the Python subsystem and terminal child processes. A macOS app launched from
    // Finder inherits only the minimal launchd PATH, so resolve the user's login-shell PATH once and reuse
    // it. On other platforms, and if resolution fails, fall back to the process PATH.
    private string ResolveChildProcessBasePath()
    {
        var processPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (!OperatingSystem.IsMacOS())
        {
            return processPath;
        }

        lock (_loginShellPathLock)
        {
            if (_resolvedLoginShellPath is not null)
            {
                return _resolvedLoginShellPath;
            }

            var loginShellPath = TryResolveLoginShellPath();
            _resolvedLoginShellPath = string.IsNullOrEmpty(loginShellPath) ? processPath : loginShellPath;
            return _resolvedLoginShellPath;
        }
    }

    private string TryResolveLoginShellPath()
    {
        try
        {
            var shell = Environment.GetEnvironmentVariable("SHELL");
            if (string.IsNullOrEmpty(shell))
            {
                shell = "/bin/zsh";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = shell,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add("-l");
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("printf '__CEL_PATH_BEGIN__%s__CEL_PATH_END__' \"$PATH\"");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return string.Empty;
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(LoginShellPathTimeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // The process may have exited between the wait timing out and the kill.
                }
                _logger.LogWarning("Timed out resolving the login shell PATH; using the process PATH instead.");
                return string.Empty;
            }

            const string beginMarker = "__CEL_PATH_BEGIN__";
            const string endMarker = "__CEL_PATH_END__";
            var startIndex = output.IndexOf(beginMarker, StringComparison.Ordinal);
            var endIndex = output.IndexOf(endMarker, StringComparison.Ordinal);
            if (startIndex < 0 || endIndex <= startIndex)
            {
                return string.Empty;
            }

            startIndex += beginMarker.Length;
            return output.Substring(startIndex, endIndex - startIndex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve the login shell PATH; using the process PATH instead.");
            return string.Empty;
        }
    }
}
