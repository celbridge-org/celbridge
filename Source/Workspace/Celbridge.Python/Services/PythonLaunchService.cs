using System.Diagnostics;
using Celbridge.FileSystem;
using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.Projects;
using Celbridge.Server;
using Celbridge.Settings;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Python.Services;

/// <summary>
/// The state of a project's installed celbridge-py tool environment, as observed on disk. Incomplete means
/// the environment is present but no longer carries the celbridge package, which is what a reinstall
/// interrupted by a running console leaves behind.
/// </summary>
internal enum ToolEnvironmentHealth
{
    Healthy,
    Missing,
    Incomplete,
}

/// <summary>
/// What a launch does about the project's shared celbridge-py tool: install it, skip because the installed
/// tool is already current, or defer because a reinstall would disturb a running console.
/// </summary>
internal enum ToolInstallDecision
{
    Install,
    Skip,
    Defer,
}

/// <summary>
/// The rule that decides whether a launch reinstalls the project's shared celbridge-py tool.
/// </summary>
internal static class ToolInstallPolicy
{
    public static ToolInstallDecision Decide(
        ToolEnvironmentHealth health,
        bool wheelHashChanged,
        bool hasRunningSessions)
    {
        if (health == ToolEnvironmentHealth.Healthy
            && !wheelHashChanged)
        {
            return ToolInstallDecision.Skip;
        }

        // A reinstall removes the tool environment that a running console is executing from. On Windows the
        // running process holds those files open, so the removal half-succeeds and leaves an environment
        // without its packages, which every later console then fails against.
        if (hasRunningSessions)
        {
            return ToolInstallDecision.Defer;
        }

        return ToolInstallDecision.Install;
    }
}

/// <summary>
/// The inputs to build a Python session's startup command: the project root, the interpreter version, the
/// extra package dependencies, and the interpreter arguments forwarded to IPython.
/// </summary>
public sealed record PythonLaunchRequest(
    string ProjectFolderPath,
    string PythonVersion,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> InterpreterArguments);

/// <summary>
/// The resolved startup: the installed celbridge-py tool to inject, and the per-console environment
/// carrying its launch defaults.
/// </summary>
public sealed record PythonStartupResult(
    string Executable,
    IReadOnlyDictionary<string, string> Environment);

/// <summary>
/// Builds the startup command and shared environment for Python sessions, owning all the Python-specific
/// launch machinery. The injected command is a bare celbridge-py; the console's interpreter version,
/// dependencies, and interpreter arguments ride per-console environment variables that the tool reads as
/// launch defaults, so retyping celbridge-py after exiting the REPL reproduces the same environment. The
/// uv and wheel locations ride the shared console environment.
/// </summary>
public interface IPythonLaunchService
{
    /// <summary>
    /// Resolves the startup command and its per-console environment for a Python session, installing
    /// support files and installing the celbridge-py tool when needed. Fails if uv or the celbridge wheel
    /// is missing, or if the tool install fails.
    /// </summary>
    Task<Result<PythonStartupResult>> BuildStartupAsync(PythonLaunchRequest request);

    /// <summary>
    /// Returns a PATH value with the project's uv tool bin folder prepended to the given base (or to the
    /// resolved child-process base PATH when null), so the installed celbridge-py command resolves in any
    /// console. Already-prepended input is returned unchanged.
    /// </summary>
    string BuildConsolePath(string projectFolderPath, string? basePath);

    /// <summary>
    /// Returns the host-integration environment every console shares (host ports, tool feature flags, the
    /// project folder, the per-project Python folders, and a PATH carrying the uv tool bin folder),
    /// creating the folders the variables point at. A celbridge-py launched from any console then behaves
    /// like a python console session.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> BuildConsoleEnvironmentAsync(string projectFolderPath);
}

public sealed class PythonLaunchService : IPythonLaunchService
{
    private const int LoginShellPathTimeoutMs = 5000;

    private const string CelbridgeToolCommand = "celbridge-py";
    private const string CelbridgePackageName = "celbridge";
    private const string SitePackagesFolderName = "site-packages";
    private const string UVCacheFolderName = "uv_cache";
    private const string UVExecutableName = "uv";
    private const string UVExecutableNameWindows = "uv.exe";
    private const string UVPythonInstallsFolderName = "uv_python_installs";
    private const string UVToolsFolderName = "uv_tools";
    private const string UVBinFolderName = "uv_bin";
    private const string IPythonCacheFolderName = "ipython";
    private const string PythonToolWheelHashFileName = "python_tool.wheelhash";

    private readonly IAppEnvironment _environmentService;
    private readonly IServerService _serverService;
    private readonly IFeatureFlags _featureFlags;
    private readonly IPythonConfigService _pythonConfigService;
    private readonly IPythonInstaller _pythonInstaller;
    private readonly ILocalFileSystem _fileSystem;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILogger<PythonLaunchService> _logger;

    // The login-shell PATH is app-global and costs a subprocess to resolve, so cache it for the app run.
    private static string? _resolvedLoginShellPath;
    private static readonly object _loginShellPathLock = new();

    // Consoles start together, so the tool install is serialized: a --force reinstall republishes the
    // celbridge-py entry point, which would otherwise vanish from under another console about to run it.
    private readonly SemaphoreSlim _toolInstallGate = new(1, 1);

    public PythonLaunchService(
        IAppEnvironment environmentService,
        IServerService serverService,
        IFeatureFlags featureFlags,
        IPythonConfigService pythonConfigService,
        IPythonInstaller pythonInstaller,
        ILocalFileSystem fileSystem,
        IWorkspaceWrapper workspaceWrapper,
        ILogger<PythonLaunchService> logger)
    {
        _environmentService = environmentService;
        _serverService = serverService;
        _featureFlags = featureFlags;
        _pythonConfigService = pythonConfigService;
        _pythonInstaller = pythonInstaller;
        _fileSystem = fileSystem;
        _workspaceWrapper = workspaceWrapper;
        _logger = logger;
    }

    public async Task<Result<PythonStartupResult>> BuildStartupAsync(PythonLaunchRequest request)
    {
        var startupTimer = Stopwatch.StartNew();

        var environmentInfo = _environmentService.GetEnvironmentInfo();
        var appVersion = environmentInfo.AppVersion;

        // The per-project Python folder under .celbridge/ holds this project's uv caches, interpreter
        // installs, tool install, and IPython profile, so one project reinstalling never disturbs another.
        var projectPythonFolder = Path.Combine(request.ProjectFolderPath, ProjectConstants.CelbridgeFolder, ProjectConstants.PythonFolder);

        var installResult = await _pythonInstaller.InstallPythonAsync(appVersion);
        if (installResult.IsFailure)
        {
            return Result<PythonStartupResult>.Fail("Failed to ensure Python support files are installed")
                .WithErrors(installResult);
        }
        var pythonFolder = installResult.Value;

        var uvFileName = OperatingSystem.IsWindows() ? UVExecutableNameWindows : UVExecutableName;
        var uvExePath = Path.Combine(pythonFolder, uvFileName);
        var uvExeInfoResult = await _fileSystem.GetInfoAsync(uvExePath);
        var uvExeExists = uvExeInfoResult.IsSuccess
            && uvExeInfoResult.Value.Kind == StorageItemKind.File;
        if (!uvExeExists)
        {
            return Result<PythonStartupResult>.Fail($"uv not found at '{uvExePath}'");
        }

        var uvCacheDir = Path.Combine(projectPythonFolder, UVCacheFolderName);
        var uvPythonInstallDir = Path.Combine(projectPythonFolder, UVPythonInstallsFolderName);
        var uvToolsFolder = Path.Combine(projectPythonFolder, UVToolsFolderName);
        var uvBinFolder = Path.Combine(projectPythonFolder, UVBinFolderName);

        // Filter blank entries, so a stray blank line cannot reach uv as an empty package specifier.
        var dependencies = request.Dependencies
            .Where(dependency => !string.IsNullOrWhiteSpace(dependency))
            .ToList();
        var interpreterArguments = request.InterpreterArguments
            .Where(argument => !string.IsNullOrWhiteSpace(argument))
            .ToList();

        var findWheelResult = await FindWheelFileAsync(pythonFolder, "celbridge");
        if (findWheelResult.IsFailure)
        {
            return Result<PythonStartupResult>.Fail("Failed to find celbridge wheel file")
                .WithErrors(findWheelResult);
        }
        var celbridgeWheelPath = findWheelResult.Value;

        var wheelHash = await FileHashHelper.HashFileContentsAsync(celbridgeWheelPath);

        // The tool install publishes the celbridge-py command into the project's uv_bin folder, which
        // every console's PATH carries, so the user can start a cel-connected REPL from a shell console
        // or a spawned terminal.
        // Consoles start together, so a console behind the one doing the install spends most of its
        // startup waiting here. Timed separately, so its total does not read as work it did.
        var gateTimer = Stopwatch.StartNew();
        await _toolInstallGate.WaitAsync();
        var gateWaitMilliseconds = gateTimer.ElapsedMilliseconds;

        try
        {
            var ensureToolResult = await EnsureCelbridgeToolAsync(
                uvExePath, uvCacheDir, uvToolsFolder, uvBinFolder,
                uvPythonInstallDir, projectPythonFolder, celbridgeWheelPath, wheelHash);
            if (ensureToolResult.IsFailure)
            {
                return Result<PythonStartupResult>.Fail("Failed to install the celbridge-py tool")
                    .WithErrors(ensureToolResult);
            }
        }
        finally
        {
            _toolInstallGate.Release();
        }

        // The injected command is a bare celbridge-py; these per-console variables are the launch
        // defaults it reads, making the tool re-exec through uv (located via the shared console
        // environment) with this console's interpreter, packages, and arguments. Lists are
        // newline-separated because PEP 508 specifiers can contain commas and semicolons. Offline mode is
        // not among them: celbridge-py measures the cache itself at launch.
        var startupEnvironment = new Dictionary<string, string>
        {
            ["CELBRIDGE_PYTHON_VERSION"] = request.PythonVersion,
        };

        if (dependencies.Count > 0)
        {
            startupEnvironment["CELBRIDGE_PYTHON_WITH"] = string.Join('\n', dependencies);
        }

        if (interpreterArguments.Count > 0)
        {
            startupEnvironment["CELBRIDGE_PYTHON_ARGS"] = string.Join('\n', interpreterArguments);
        }

        _logger.LogDebug("Built Python startup in {DurationMs}ms (gate wait {GateWaitMs}ms): {Command} with launch defaults {Environment}",
            startupTimer.ElapsedMilliseconds,
            gateWaitMilliseconds,
            CelbridgeToolCommand,
            string.Join(' ', startupEnvironment.Select(pair => $"{pair.Key}={pair.Value.Replace('\n', ';')}")));

        var result = new PythonStartupResult(CelbridgeToolCommand, startupEnvironment);
        return result;
    }

    public string BuildConsolePath(string projectFolderPath, string? basePath)
    {
        var projectPythonFolder = Path.Combine(projectFolderPath, ProjectConstants.CelbridgeFolder, ProjectConstants.PythonFolder);
        var uvBinFolder = Path.Combine(projectPythonFolder, UVBinFolderName);
        var resolvedBase = string.IsNullOrEmpty(basePath) ? ResolveChildProcessBasePath() : basePath;

        return resolvedBase.Contains(uvBinFolder, StringComparison.OrdinalIgnoreCase)
            ? resolvedBase
            : uvBinFolder + Path.PathSeparator + resolvedBase;
    }

    public async Task<IReadOnlyDictionary<string, string>> BuildConsoleEnvironmentAsync(string projectFolderPath)
    {
        var projectPythonFolder = Path.Combine(projectFolderPath, ProjectConstants.CelbridgeFolder, ProjectConstants.PythonFolder);

        var uvPythonInstallDir = Path.Combine(projectPythonFolder, UVPythonInstallsFolderName);
        await _fileSystem.CreateFolderAsync(uvPythonInstallDir);

        var ipythonDir = Path.Combine(projectPythonFolder, IPythonCacheFolderName);
        await _fileSystem.CreateFolderAsync(ipythonDir);

        var environmentInfo = _environmentService.GetEnvironmentInfo();
        var celbridgeVersion = environmentInfo.Configuration == "Debug"
            ? $"{environmentInfo.AppVersion} (Debug)"
            : $"{environmentInfo.AppVersion}";

        var environment = new Dictionary<string, string>
        {
            ["UV_PYTHON_INSTALL_DIR"] = uvPythonInstallDir,
            ["PATH"] = BuildConsolePath(projectFolderPath, null),
            ["CELBRIDGE_MCP_PORT"] = _serverService.Port.ToString(),
            ["CELBRIDGE_MCP_TOOLS"] = _featureFlags.IsEnabled(FeatureFlagConstants.McpTools) ? "1" : "0",
            ["CELBRIDGE_WEB_ACCESS_TOOLS"] = _featureFlags.IsEnabled(FeatureFlagConstants.WebAccessTools) ? "1" : "0",
            ["CELBRIDGE_PROJECT_FOLDER"] = projectFolderPath,
            ["CELBRIDGE_VERSION"] = celbridgeVersion,
            ["CELBRIDGE_IPYTHON_DIR"] = ipythonDir,
            ["CELBRIDGE_UV_CACHE_DIR"] = Path.Combine(projectPythonFolder, UVCacheFolderName),
        };

        // The bootstrapper variables: where a typed celbridge-py finds uv and the celbridge wheel when its
        // launch options make it re-exec through uv. Only set once the support files are installed; before
        // that no celbridge-py tool exists to consume them.
        var pythonFolder = _pythonInstaller.PythonFolderPath;

        var uvFileName = OperatingSystem.IsWindows() ? UVExecutableNameWindows : UVExecutableName;
        var uvExePath = Path.Combine(pythonFolder, uvFileName);
        var uvExeInfoResult = await _fileSystem.GetInfoAsync(uvExePath);
        var uvExeExists = uvExeInfoResult.IsSuccess
            && uvExeInfoResult.Value.Kind == StorageItemKind.File;
        if (uvExeExists)
        {
            environment["CELBRIDGE_UV"] = uvExePath;
        }

        var findWheelResult = await FindWheelFileAsync(pythonFolder, "celbridge");
        if (findWheelResult.IsSuccess)
        {
            environment["CELBRIDGE_WHEEL"] = findWheelResult.Value;
        }

        return environment;
    }

    // The tool environment is shared by every console in the project, so it is keyed on the wheel hash
    // alone. Keying it on anything a console chooses (its dependencies, its interpreter version) is what
    // makes opening a second console reinstall a tool the first console is running from.
    private async Task<Result> EnsureCelbridgeToolAsync(
        string uvExePath,
        string uvCacheDir,
        string uvToolsFolder,
        string uvBinFolder,
        string uvPythonInstallDir,
        string projectPythonFolder,
        string celbridgeWheelPath,
        string wheelHash)
    {
        var health = await CheckToolEnvironmentHealthAsync(uvToolsFolder, uvBinFolder);
        var installedWheelHash = await LoadInstalledToolWheelHashAsync(projectPythonFolder);
        var wheelHashChanged = installedWheelHash != wheelHash;
        var hasRunningSessions = HasRunningConsoleSessions();

        _logger.LogDebug(
            "Python tool key: wheelHash={WheelHash} installed={InstalledWheelHash} changed={WheelHashChanged} health={Health}",
            wheelHash,
            string.IsNullOrEmpty(installedWheelHash) ? "(none)" : installedWheelHash,
            wheelHashChanged,
            health);

        var decision = ToolInstallPolicy.Decide(health, wheelHashChanged, hasRunningSessions);
        if (decision == ToolInstallDecision.Skip)
        {
            _logger.LogInformation("Python tool install skipped: the installed tool is already current");

            return Result.Ok();
        }

        if (decision == ToolInstallDecision.Defer)
        {
            _logger.LogWarning(
                "Python tool install deferred because console sessions are running (health={Health}, wheelHashChanged={WheelHashChanged}). Launching against the existing tool.",
                health,
                wheelHashChanged);

            return Result.Ok();
        }

        // A fixed interpreter version, so the wheel hash fully describes the installed tool. The tool
        // environment only runs the bootstrap shim; the version the REPL runs on is chosen by the inner
        // uv run.
        var toolPythonVersion = _pythonConfigService.DefaultPythonVersion;

        var installResult = await InstallCelbridgeToolAsync(
            uvExePath, uvCacheDir, uvToolsFolder, uvBinFolder,
            uvPythonInstallDir, toolPythonVersion, celbridgeWheelPath);
        if (installResult.IsFailure)
        {
            return installResult;
        }

        await SaveInstalledToolWheelHashAsync(projectPythonFolder, wheelHash);

        return Result.Ok();
    }

    // Health is read off the disk rather than from a recorded flag, so an environment gutted by a failed
    // install is repaired on the next launch without the user having to clear anything.
    private async Task<ToolEnvironmentHealth> CheckToolEnvironmentHealthAsync(string uvToolsFolder, string uvBinFolder)
    {
        var toolEnvironmentFolder = Path.Combine(uvToolsFolder, CelbridgePackageName);
        var toolEnvironmentInfoResult = await _fileSystem.GetInfoAsync(toolEnvironmentFolder);
        var toolEnvironmentExists = toolEnvironmentInfoResult.IsSuccess
            && toolEnvironmentInfoResult.Value.Kind == StorageItemKind.Folder;
        if (!toolEnvironmentExists)
        {
            return ToolEnvironmentHealth.Missing;
        }

        var entryPointName = OperatingSystem.IsWindows() ? CelbridgeToolCommand + ".exe" : CelbridgeToolCommand;
        var entryPointPath = Path.Combine(uvBinFolder, entryPointName);
        var entryPointInfoResult = await _fileSystem.GetInfoAsync(entryPointPath);
        var entryPointExists = entryPointInfoResult.IsSuccess
            && entryPointInfoResult.Value.Kind == StorageItemKind.File;
        if (!entryPointExists)
        {
            return ToolEnvironmentHealth.Missing;
        }

        var sitePackagesFolder = await FindSitePackagesFolderAsync(toolEnvironmentFolder);
        if (sitePackagesFolder is null)
        {
            return ToolEnvironmentHealth.Incomplete;
        }

        var packageFolder = Path.Combine(sitePackagesFolder, CelbridgePackageName);
        var packageInfoResult = await _fileSystem.GetInfoAsync(packageFolder);
        var packageExists = packageInfoResult.IsSuccess
            && packageInfoResult.Value.Kind == StorageItemKind.Folder;
        if (!packageExists)
        {
            return ToolEnvironmentHealth.Incomplete;
        }

        return ToolEnvironmentHealth.Healthy;
    }

    // A uv tool environment is a venv: Windows puts site-packages under Lib, other platforms under
    // lib/pythonX.Y, so the interpreter folder is discovered rather than assumed.
    private async Task<string?> FindSitePackagesFolderAsync(string toolEnvironmentFolder)
    {
        var windowsSitePackages = Path.Combine(toolEnvironmentFolder, "Lib", SitePackagesFolderName);
        var windowsInfoResult = await _fileSystem.GetInfoAsync(windowsSitePackages);
        var windowsSitePackagesExists = windowsInfoResult.IsSuccess
            && windowsInfoResult.Value.Kind == StorageItemKind.Folder;
        if (windowsSitePackagesExists)
        {
            return windowsSitePackages;
        }

        var libFolder = Path.Combine(toolEnvironmentFolder, "lib");
        var enumerateResult = await _fileSystem.EnumerateAsync(libFolder, "python*", recursive: false);
        if (enumerateResult.IsFailure)
        {
            return null;
        }

        foreach (var entry in enumerateResult.Value)
        {
            if (!entry.IsFolder)
            {
                continue;
            }

            var candidateFolder = Path.Combine(entry.FullPath, SitePackagesFolderName);
            var candidateInfoResult = await _fileSystem.GetInfoAsync(candidateFolder);
            var candidateExists = candidateInfoResult.IsSuccess
                && candidateInfoResult.Value.Kind == StorageItemKind.Folder;
            if (candidateExists)
            {
                return candidateFolder;
            }
        }

        return null;
    }

    // PythonLaunchService is an application singleton, so the workspace-scoped session service is resolved
    // at call time. The console requesting the launch is still starting and so does not count itself.
    private bool HasRunningConsoleSessions()
    {
        if (!_workspaceWrapper.HasWorkspaceService)
        {
            return false;
        }

        return _workspaceWrapper.WorkspaceService.ConsoleService.Sessions.HasRunningSessions;
    }

    private async Task<Result> InstallCelbridgeToolAsync(
        string uvExePath,
        string uvCacheDir,
        string uvToolsFolder,
        string uvBinFolder,
        string uvPythonInstallDir,
        string pythonVersion,
        string celbridgeWheelPath)
    {
        _logger.LogInformation("Installing celbridge as uv tool with Python {PythonVersion}", pythonVersion);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = uvExePath,
            WorkingDirectory = Path.GetDirectoryName(uvExePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var toolInstallArguments = new[]
        {
            "tool",
            "install",
            "--force",
            "--cache-dir", uvCacheDir,
            "--python", pythonVersion,
            "--managed-python",
            celbridgeWheelPath,
        };
        foreach (var argument in toolInstallArguments)
        {
            processStartInfo.ArgumentList.Add(argument);
        }

        _logger.LogDebug("uv tool install command: {FileName} {Arguments}", uvExePath, string.Join(' ', toolInstallArguments));

        processStartInfo.Environment["UV_TOOL_DIR"] = uvToolsFolder;
        processStartInfo.Environment["UV_TOOL_BIN_DIR"] = uvBinFolder;
        processStartInfo.Environment["UV_PYTHON_INSTALL_DIR"] = uvPythonInstallDir;

        var installTimer = Stopwatch.StartNew();

        using var process = Process.Start(processStartInfo);
        if (process is null)
        {
            return Result.Fail($"Failed to start uv at '{uvExePath}'");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);

            return Result.Fail("uv tool install timed out after 2 minutes");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        // Launching against a half-removed tool environment turns an install failure into an unrelated
        // Python traceback, so the failure is surfaced here instead.
        if (process.ExitCode != 0)
        {
            _logger.LogError("uv tool install exited with code {ExitCode} after {DurationMs}ms. Stderr: {Stderr}. Stdout: {Stdout}",
                process.ExitCode, installTimer.ElapsedMilliseconds, stderr, stdout);

            return Result.Fail($"uv tool install exited with code {process.ExitCode}. {stderr.Trim()}");
        }

        _logger.LogInformation("celbridge tool installed successfully in {DurationMs}ms", installTimer.ElapsedMilliseconds);

        return Result.Ok();
    }

    // The wheel hash the installed tool was built from. Recorded in the project's Python folder rather than
    // in memory, so a restarted application does not reinstall a tool that is already current.
    private async Task<string?> LoadInstalledToolWheelHashAsync(string projectPythonFolder)
    {
        var filePath = Path.Combine(projectPythonFolder, PythonToolWheelHashFileName);
        var infoResult = await _fileSystem.GetInfoAsync(filePath);
        var fileExists = infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.File;
        if (!fileExists)
        {
            return null;
        }

        var readResult = await _fileSystem.ReadAllTextAsync(filePath);
        if (readResult.IsFailure)
        {
            return null;
        }

        return readResult.Value.Trim();
    }

    private async Task SaveInstalledToolWheelHashAsync(string projectPythonFolder, string wheelHash)
    {
        var createFolderResult = await _fileSystem.CreateFolderAsync(projectPythonFolder);
        if (createFolderResult.IsFailure)
        {
            _logger.LogWarning("Failed to record the installed Python tool wheel hash: {Error}", createFolderResult.FirstErrorMessage);
            return;
        }

        var filePath = Path.Combine(projectPythonFolder, PythonToolWheelHashFileName);
        var writeResult = await _fileSystem.WriteAllTextAsync(filePath, wheelHash);
        if (writeResult.IsFailure)
        {
            _logger.LogWarning("Failed to record the installed Python tool wheel hash: {Error}", writeResult.FirstErrorMessage);
        }
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
