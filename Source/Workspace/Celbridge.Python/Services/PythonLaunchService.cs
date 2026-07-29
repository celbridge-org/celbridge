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
/// The inputs to build a Python launch: the project root, the interpreter version, the extra package
/// dependencies, the interpreter arguments, and the environment variables the caller wants merged on top
/// (the user's [session.environment] plus CELBRIDGE_RPC_PORT and CELBRIDGE_SESSION_TOKEN).
/// </summary>
public sealed record PythonLaunchRequest(
    string ProjectFolderPath,
    string PythonVersion,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> InterpreterArguments,
    IReadOnlyDictionary<string, string> ExtraEnvironment);

/// <summary>
/// The resolved launch: the command line to run, the environment to inject, and the config fingerprint plus
/// the project Python folder so the caller can persist the fingerprint once the session proves it launches.
/// </summary>
public sealed record PythonLaunchResult(
    string CommandLine,
    IReadOnlyDictionary<string, string> Environment,
    string Fingerprint,
    string ProjectPythonFolder);

/// <summary>
/// Builds the uv command line and environment for a Python session, owning all the Python-specific launch
/// machinery.
/// </summary>
public interface IPythonLaunchService
{
    /// <summary>
    /// Resolves the command line and environment for a Python session, installing support files and
    /// computing the offline fingerprint. Fails if uv or the celbridge wheel is missing.
    /// </summary>
    Task<Result<PythonLaunchResult>> BuildLaunchAsync(PythonLaunchRequest request);

    /// <summary>
    /// Persists the config fingerprint once a session has proven it launches, enabling offline mode next
    /// run. Failures are swallowed (the next run just uses online mode).
    /// </summary>
    Task SaveFingerprintAsync(string projectPythonFolder, string fingerprint);
}

/// <summary>
/// Builds the uv command line and environment for a Python session, owning all the Python-specific launch
/// machinery: the uv command, the per-project uv caches, wheel discovery, the offline fingerprint, and the
/// IPython profile.
/// </summary>
public sealed class PythonLaunchService : IPythonLaunchService
{
    private const int PythonLogMaxFiles = 10;
    private const int LoginShellPathTimeoutMs = 5000;

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

    public async Task<Result<PythonLaunchResult>> BuildLaunchAsync(PythonLaunchRequest request)
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
            return Result<PythonLaunchResult>.Fail("Failed to ensure Python support files are installed")
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
            return Result<PythonLaunchResult>.Fail($"uv not found at '{uvExePath}'");
        }

        var uvCacheDir = Path.Combine(projectPythonFolder, UVCacheFolderName);
        var uvPythonInstallDir = Path.Combine(projectPythonFolder, UVPythonInstallsFolderName);
        await _fileSystem.CreateFolderAsync(uvPythonInstallDir);

        var ipythonDir = Path.Combine(projectPythonFolder, IPythonCacheFolderName);
        await _fileSystem.CreateFolderAsync(ipythonDir);

        var configuration = environmentInfo.Configuration;
        var celbridgeVersion = configuration == "Debug" ? $"{appVersion} (Debug)" : $"{appVersion}";

        var pythonLogFolder = Path.Combine(request.ProjectFolderPath, ProjectConstants.CelbridgeFolder, ProjectConstants.LogsFolder);

        var uvToolsFolder = Path.Combine(projectPythonFolder, UVToolsFolderName);
        var uvBinFolder = Path.Combine(projectPythonFolder, UVBinFolderName);
        var currentPath = ResolveChildProcessBasePath();
        var terminalPath = currentPath.Contains(uvBinFolder, StringComparison.OrdinalIgnoreCase)
            ? currentPath
            : uvBinFolder + Path.PathSeparator + currentPath;

        // The session environment: the uv interpreter install dir and PATH, plus the host-integration
        // variables the celbridge connector needs to dial back into the workspace.
        var environment = new Dictionary<string, string>
        {
            ["UV_PYTHON_INSTALL_DIR"] = uvPythonInstallDir,
            ["PATH"] = terminalPath,
            ["CELBRIDGE_MCP_PORT"] = _serverService.Port.ToString(),
            ["CELBRIDGE_MCP_TOOLS"] = _featureFlags.IsEnabled(FeatureFlagConstants.McpTools) ? "1" : "0",
            ["CELBRIDGE_WEB_ACCESS_TOOLS"] = _featureFlags.IsEnabled(FeatureFlagConstants.WebAccessTools) ? "1" : "0",
            ["CELBRIDGE_PROJECT_FOLDER"] = request.ProjectFolderPath,
            ["CELBRIDGE_VERSION"] = celbridgeVersion,
            ["CELBRIDGE_IPYTHON_DIR"] = ipythonDir,
            ["PYTHON_LOG_LEVEL"] = "DEBUG",
            ["PYTHON_LOG_DIR"] = pythonLogFolder,
            ["PYTHON_LOG_MAX_FILES"] = PythonLogMaxFiles.ToString(),
        };

        // Extra dependencies become --with pairs, deduped by uv against the per-project wheel cache.
        var packageArgs = new List<string>();
        foreach (var dependency in request.Dependencies)
        {
            if (string.IsNullOrWhiteSpace(dependency))
            {
                continue;
            }
            packageArgs.Add("--with");
            packageArgs.Add(dependency);
        }

        var findWheelResult = await FindWheelFileAsync(pythonFolder, "celbridge");
        if (findWheelResult.IsFailure)
        {
            return Result<PythonLaunchResult>.Fail("Failed to find celbridge wheel file")
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
            request.Dependencies,
            request.InterpreterArguments,
            installStateHash);
        var savedFingerprint = await LoadSavedFingerprintAsync(projectPythonFolder);
        var useOfflineMode = currentFingerprint == savedFingerprint;

        var uvBinFolderInfo = await _fileSystem.GetInfoAsync(uvBinFolder);
        var uvBinFolderExists = uvBinFolderInfo.IsSuccess
            && uvBinFolderInfo.Value.Kind == StorageItemKind.Folder;
        var shouldInstallTool = !useOfflineMode || !uvBinFolderExists;
        if (shouldInstallTool)
        {
            await InstallCelbridgeToolAsync(
                uvExePath, uvCacheDir, uvToolsFolder, uvBinFolder,
                uvPythonInstallDir, request.PythonVersion, celbridgeWheelPath);
        }

        var uvBuilder = new CommandLineBuilder(uvExePath)
            .Add("run")
            .Add("--cache-dir", uvCacheDir);

        if (useOfflineMode)
        {
            uvBuilder.Add("--offline");
        }

        uvBuilder
            .Add("--no-project")
            .Add("--python", request.PythonVersion)
            .Add("--managed-python")
            .Add("--with", celbridgeWheelPath);

        uvBuilder.Add(packageArgs.ToArray());

        uvBuilder
            .Add("python")
            .Add("-m", "celbridge");

        // Interpreter arguments follow the interpreter invocation, reaching IPython via celbridge's
        // __main__ (which forwards sys.argv).
        foreach (var interpreterArgument in request.InterpreterArguments)
        {
            if (!string.IsNullOrWhiteSpace(interpreterArgument))
            {
                uvBuilder.Add(interpreterArgument);
            }
        }

        var commandLine = uvBuilder.ToString();

        // The caller's environment (user config plus the host-binding variables) wins over the base env.
        foreach (var pair in request.ExtraEnvironment)
        {
            environment[pair.Key] = pair.Value;
        }

        _logger.LogDebug(
            "Built Python launch: offline={Offline} command={Command}",
            useOfflineMode,
            commandLine);

        var result = new PythonLaunchResult(commandLine, environment, currentFingerprint, projectPythonFolder);
        return result;
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
