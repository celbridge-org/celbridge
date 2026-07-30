using System.Diagnostics;
using System.Text;
using Celbridge.FileSystem;
using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.Projects;
using Celbridge.Server;
using Celbridge.Settings;
using Celbridge.Utilities;

namespace Celbridge.Python.Services;

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
/// The resolved startup: the installed celbridge-py tool to inject, the per-console environment carrying
/// its launch defaults, and the config fingerprint plus the project Python folder so the caller can
/// persist the fingerprint once the session proves it launches.
/// </summary>
public sealed record PythonStartupResult(
    string Executable,
    IReadOnlyDictionary<string, string> Environment,
    string Fingerprint,
    string ProjectPythonFolder);

/// <summary>
/// Builds the startup command and shared environment for Python sessions, owning all the Python-specific
/// launch machinery. The injected command is a bare celbridge-py; the console's interpreter version,
/// dependencies, interpreter arguments, and offline mode ride per-console environment variables that the
/// tool reads as launch defaults, so retyping celbridge-py after exiting the REPL reproduces the same
/// environment. The uv and wheel locations ride the shared console environment.
/// </summary>
public interface IPythonLaunchService
{
    /// <summary>
    /// Resolves the startup command and its per-console environment for a Python session, installing
    /// support files, installing the celbridge-py tool when needed, and computing the offline
    /// fingerprint. Fails if uv or the celbridge wheel is missing.
    /// </summary>
    Task<Result<PythonStartupResult>> BuildStartupAsync(PythonLaunchRequest request);

    /// <summary>
    /// Persists the config fingerprint once a session has proven it launches, enabling offline mode next
    /// run. Failures are swallowed (the next run just uses online mode).
    /// </summary>
    Task SaveFingerprintAsync(string projectPythonFolder, string fingerprint);

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
    private const int PythonLogMaxFiles = 10;
    private const int LoginShellPathTimeoutMs = 5000;

    private const string CelbridgeToolCommand = "celbridge-py";
    private const string UVCacheFolderName = "uv_cache";
    private const string UVExecutableName = "uv";
    private const string UVExecutableNameWindows = "uv.exe";
    private const string UVPythonInstallsFolderName = "uv_python_installs";
    private const string UVToolsFolderName = "uv_tools";
    private const string UVBinFolderName = "uv_bin";
    private const string IPythonCacheFolderName = "ipython";
    private const string PythonFingerprintFileName = "python_config.fingerprint";

    private readonly IAppEnvironment _environmentService;
    private readonly IServerService _serverService;
    private readonly IFeatureFlags _featureFlags;
    private readonly IPythonInstaller _pythonInstaller;
    private readonly ILocalFileSystem _fileSystem;
    private readonly ILogger<PythonLaunchService> _logger;

    // The login-shell PATH is app-global and costs a subprocess to resolve, so cache it for the app run.
    private static string? _resolvedLoginShellPath;
    private static readonly object _loginShellPathLock = new();

    // Consoles start together, so the tool install is serialized: a --force reinstall republishes the
    // celbridge-py entry point, which would otherwise vanish from under another console about to run it.
    private readonly SemaphoreSlim _toolInstallGate = new(1, 1);
    private string? _installedToolFingerprint;

    public PythonLaunchService(
        IAppEnvironment environmentService,
        IServerService serverService,
        IFeatureFlags featureFlags,
        IPythonInstaller pythonInstaller,
        ILocalFileSystem fileSystem,
        ILogger<PythonLaunchService> logger)
    {
        _environmentService = environmentService;
        _serverService = serverService;
        _featureFlags = featureFlags;
        _pythonInstaller = pythonInstaller;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<Result<PythonStartupResult>> BuildStartupAsync(PythonLaunchRequest request)
    {
        var environmentInfo = _environmentService.GetEnvironmentInfo();
        var appVersion = environmentInfo.AppVersion;

        // The per-project Python folder under .celbridge/ holds this project's uv caches, interpreter
        // installs, tool install, IPython profile, and the config fingerprint, so one project reinstalling
        // never disturbs another.
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

        // Filter blank entries once, so the command and the fingerprint see the same effective config and
        // a stray blank line cannot flip a launch to online mode.
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

        // The fingerprint combines the config with a hash of the wheel contents and a structural hash of
        // the stable parts of the install, so an unchanged config launches in offline mode.
        var wheelHash = await FileHashHelper.HashFileContentsAsync(celbridgeWheelPath);
        var installStateHash = await ComputeInstallStateHashAsync(pythonFolder, projectPythonFolder);
        var currentFingerprint = ComputeConfigFingerprint(
            appVersion,
            request.PythonVersion,
            celbridgeWheelPath,
            wheelHash,
            dependencies,
            interpreterArguments,
            installStateHash);
        var savedFingerprint = await LoadSavedFingerprintAsync(projectPythonFolder);
        var useOfflineMode = currentFingerprint == savedFingerprint;

        // Log the fingerprint components so an unexpected offline/online transition can be diagnosed
        // from the log alone.
        _logger.LogDebug(
            "Python fingerprint: wheelHash={WheelHash} installStateHash={InstallStateHash} current={CurrentFingerprint} saved={SavedFingerprint}",
            wheelHash,
            installStateHash,
            currentFingerprint,
            string.IsNullOrEmpty(savedFingerprint) ? "(none)" : savedFingerprint);
        if (useOfflineMode)
        {
            _logger.LogInformation("Python launch mode: offline (config fingerprint unchanged)");
        }
        else
        {
            var onlineReason = string.IsNullOrEmpty(savedFingerprint)
                ? "no saved fingerprint, first run"
                : "config changed since last run";
            _logger.LogInformation("Python launch mode: online ({Reason})", onlineReason);
        }

        // The tool install publishes the celbridge-py command into the project's uv_bin folder, which
        // every console's PATH carries, so the user can start a cel-connected REPL from a shell console
        // or a spawned terminal.
        await _toolInstallGate.WaitAsync();
        try
        {
            var uvBinFolderInfo = await _fileSystem.GetInfoAsync(uvBinFolder);
            var uvBinFolderExists = uvBinFolderInfo.IsSuccess
                && uvBinFolderInfo.Value.Kind == StorageItemKind.Folder;

            // A console that waited on the gate inherits the install the console ahead of it just did, as
            // long as they resolved the same config.
            var alreadyInstalled = _installedToolFingerprint == currentFingerprint
                && uvBinFolderExists;
            var shouldInstallTool = !alreadyInstalled
                && (!useOfflineMode || !uvBinFolderExists);
            if (shouldInstallTool)
            {
                await InstallCelbridgeToolAsync(
                    uvExePath, uvCacheDir, uvToolsFolder, uvBinFolder,
                    uvPythonInstallDir, request.PythonVersion, celbridgeWheelPath);

                _installedToolFingerprint = currentFingerprint;
            }
        }
        finally
        {
            _toolInstallGate.Release();
        }

        // The injected command is a bare celbridge-py; these per-console variables are the launch
        // defaults it reads, making the tool re-exec through uv (located via the shared console
        // environment) with this console's interpreter, packages, and arguments. Lists are
        // newline-separated because PEP 508 specifiers can contain commas and semicolons.
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

        if (useOfflineMode)
        {
            startupEnvironment["CELBRIDGE_PYTHON_OFFLINE"] = "1";
        }

        _logger.LogDebug("Built Python startup: {Command} with launch defaults {Environment}",
            CelbridgeToolCommand,
            string.Join(' ', startupEnvironment.Select(pair => $"{pair.Key}={pair.Value.Replace('\n', ';')}")));

        var result = new PythonStartupResult(CelbridgeToolCommand, startupEnvironment, currentFingerprint, projectPythonFolder);
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

        var pythonLogFolder = Path.Combine(projectFolderPath, ProjectConstants.CelbridgeFolder, ProjectConstants.LogsFolder);

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
            ["CELBRIDGE_PYTHON_LOG_LEVEL"] = "DEBUG",
            ["CELBRIDGE_PYTHON_LOG_DIR"] = pythonLogFolder,
            ["CELBRIDGE_PYTHON_LOG_MAX_FILES"] = PythonLogMaxFiles.ToString(),
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

    public async Task SaveFingerprintAsync(string projectPythonFolder, string fingerprint)
    {
        // Non-critical: failures here just mean the next run uses online mode.
        var createFolderResult = await _fileSystem.CreateFolderAsync(projectPythonFolder);
        if (createFolderResult.IsFailure)
        {
            return;
        }

        var filePath = Path.Combine(projectPythonFolder, PythonFingerprintFileName);
        await _fileSystem.WriteAllTextAsync(filePath, fingerprint);
    }

    private async Task InstallCelbridgeToolAsync(
        string uvExePath,
        string uvCacheDir,
        string uvToolsFolder,
        string uvBinFolder,
        string uvPythonInstallDir,
        string pythonVersion,
        string celbridgeWheelPath)
    {
        _logger.LogInformation("Installing celbridge as uv tool");

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

        using var process = Process.Start(processStartInfo);
        if (process != null)
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                await process.WaitForExitAsync(timeoutCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("uv tool install timed out after 2 minutes, killing process");
                process.Kill(entireProcessTree: true);
                return;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("uv tool install exited with code {ExitCode}. Stderr: {Stderr}. Stdout: {Stdout}",
                    process.ExitCode, stderr, stdout);
            }
            else
            {
                _logger.LogInformation("celbridge tool installed successfully");
            }
        }
    }

    private async Task<string> ComputeInstallStateHashAsync(string appPythonFolder, string projectPythonFolder)
    {
        var sb = new StringBuilder();

        var uvExeName = OperatingSystem.IsWindows() ? UVExecutableNameWindows : UVExecutableName;
        var uvExePath = Path.Combine(appPythonFolder, uvExeName);
        var uvExeInfoResult = await _fileSystem.GetInfoAsync(uvExePath);
        if (uvExeInfoResult.IsSuccess
            && uvExeInfoResult.Value.Kind == StorageItemKind.File)
        {
            sb.AppendLine($"uv|{uvExeInfoResult.Value.Size}");
        }
        else
        {
            sb.AppendLine("uv|missing");
        }

        var installsHash = await FileHashHelper.HashFolderStructureAsync(
            Path.Combine(projectPythonFolder, UVPythonInstallsFolderName),
            maxDepth: 1);
        sb.AppendLine($"installs|{installsHash}");

        var uvCacheDir = Path.Combine(projectPythonFolder, UVCacheFolderName);
        var uvCacheInfoResult = await _fileSystem.GetInfoAsync(uvCacheDir);
        if (uvCacheInfoResult.IsSuccess
            && uvCacheInfoResult.Value.Kind == StorageItemKind.Folder)
        {
            var wheelsFolders = new List<string>();
            var enumerateFoldersResult = await _fileSystem.EnumerateAsync(uvCacheDir, "*", recursive: false);
            if (enumerateFoldersResult.IsSuccess)
            {
                foreach (var entry in enumerateFoldersResult.Value)
                {
                    if (!entry.IsFolder)
                    {
                        continue;
                    }
                    var folderName = Path.GetFileName(entry.FullPath);
                    if (folderName.StartsWith("wheels-v", StringComparison.Ordinal))
                    {
                        wheelsFolders.Add(entry.FullPath);
                    }
                }
                wheelsFolders.Sort(StringComparer.Ordinal);
            }

            foreach (var wheelsFolder in wheelsFolders)
            {
                var folderName = Path.GetFileName(wheelsFolder);
                var wheelsHash = await FileHashHelper.HashFolderStructureAsync(wheelsFolder, maxDepth: 3);
                sb.AppendLine($"wheels|{folderName}|{wheelsHash}");
            }
        }

        return FileHashHelper.HashString(sb.ToString());
    }

    private static string ComputeConfigFingerprint(
        string appVersion,
        string pythonVersion,
        string celbridgeWheelPath,
        string wheelHash,
        IReadOnlyList<string> dependencies,
        IReadOnlyList<string> interpreterArguments,
        string installStateHash)
    {
        var sb = new StringBuilder();
        sb.AppendLine(appVersion);
        sb.AppendLine(pythonVersion);
        sb.AppendLine(Path.GetFileName(celbridgeWheelPath));
        sb.AppendLine(wheelHash);

        foreach (var dependency in dependencies)
        {
            sb.AppendLine(dependency);
        }

        foreach (var interpreterArgument in interpreterArguments)
        {
            sb.AppendLine($"arg|{interpreterArgument}");
        }

        sb.AppendLine(installStateHash);

        return FileHashHelper.HashString(sb.ToString());
    }

    private async Task<string?> LoadSavedFingerprintAsync(string projectPythonFolder)
    {
        var filePath = Path.Combine(projectPythonFolder, PythonFingerprintFileName);
        var fingerprintInfoResult = await _fileSystem.GetInfoAsync(filePath);
        var fingerprintExists = fingerprintInfoResult.IsSuccess
            && fingerprintInfoResult.Value.Kind == StorageItemKind.File;
        if (!fingerprintExists)
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
