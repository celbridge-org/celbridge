using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Celbridge.Platform;
using Celbridge.Console;
using Celbridge.FileSystem;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Server;
using Celbridge.Settings;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Python.Services;

public class PythonService : IPythonService, IDisposable
{
    private const int PythonLogMaxFiles = 10;

    // Folder and file names
    private const string UVCacheFolderName = "uv_cache";
    private const string UVExecutableName = "uv";
    private const string UVExecutableNameWindows = "uv.exe";
    private const string UVPythonInstallsFolderName = "uv_python_installs";
    private const string UVToolsFolderName = "uv_tools";
    private const string UVBinFolderName = "uv_bin";
    private const string IPythonCacheFolderName = "ipython";
    private const string PythonFingerprintFileName = "python_config.fingerprint";

    private readonly IProjectService _projectService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IAppEnvironment _environmentService;
    private readonly IServerService _serverService;
    private readonly IMessengerService _messengerService;
    private readonly IFeatureFlags _featureFlags;
    private readonly IPythonInstaller _pythonInstaller;
    private readonly ILocalFileSystem _fileSystem;
    private readonly ILogger<PythonService> _logger;
    private readonly ITcpTransport _tcpTransport;
    private CancellationTokenSource? _rpcCancellationTokenSource;
    private string _pendingFingerprint = string.Empty;
    private string _pendingProjectPythonFolder = string.Empty;
    private bool _fingerprintSaved;
    private volatile bool _hadConnection;

    public PythonService(
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper,
        IAppEnvironment environmentService,
        IServerService serverService,
        IMessengerService messengerService,
        IFeatureFlags featureFlags,
        IPythonInstaller pythonInstaller,
        ILocalFileSystem fileSystem,
        ILogger<PythonService> logger,
        ITcpTransport tcpTransport)
    {
        _projectService = projectService;
        _workspaceWrapper = workspaceWrapper;
        _environmentService = environmentService;
        _serverService = serverService;
        _messengerService = messengerService;
        _featureFlags = featureFlags;
        _pythonInstaller = pythonInstaller;
        _fileSystem = fileSystem;
        _logger = logger;
        _tcpTransport = tcpTransport;
    }

    public bool IsPythonHostAvailable { get; private set; } = false;

    public async Task<Result> InitializePython()
    {
        try
        {
            var project = _projectService.CurrentProject;
            if (project is null)
            {
                return Result.Fail("Failed to run python as no project is loaded");
            }

            // Get the project file name for error messages
            var projectFileName = Path.GetFileName(project.ProjectFilePath);

            // Read python version from project config
            var pythonConfig = project.Config.Project;
            if (pythonConfig is null)
            {
                var errorMessage = new ConsoleErrorMessage(ConsoleErrorType.InvalidProjectConfig, projectFileName);
                _messengerService.Send(errorMessage);
                return Result.Fail($"Project section not specified in project config '{projectFileName}'");
            }

            // Note: uv run accepts a specific python version (e.g. "3.12") or a range descriptor (e.g. ">=3.12")
            var pythonVersion = pythonConfig.RequiresPython;
            if (string.IsNullOrWhiteSpace(pythonVersion))
            {
                var errorMessage = new ConsoleErrorMessage(ConsoleErrorType.InvalidProjectConfig, projectFileName);
                _messengerService.Send(errorMessage);
                return Result.Fail($"Python version not specified in requires-python field in project config '{projectFileName}'");
            }

            // Ensure that python support files are installed
            var workingDir = project.ProjectFolderPath;

            var environmentInfo = _environmentService.GetEnvironmentInfo();
            var appVersion = environmentInfo.AppVersion;

            // The per-project Python folder under .celbridge/ holds this project's uv caches,
            // interpreter installs, tool install, IPython profile, and the config fingerprint.
            // Keeping these per-project means one project reinstalling never disturbs another.
            var projectPythonFolder = Path.Combine(workingDir, ProjectConstants.CelbridgeFolder, ProjectConstants.PythonFolder);
            var savedFingerprint = await LoadSavedFingerprintAsync(projectPythonFolder);

            var installResult = await _pythonInstaller.InstallPythonAsync(appVersion);
            if (installResult.IsFailure)
            {
                var errorMessage = new ConsoleErrorMessage(
                    ConsoleErrorType.PythonHostPreInitError,
                    "Failed to install Python support files");
                _messengerService.Send(errorMessage);
                return Result.Fail("Failed to ensure Python support files are installed")
                    .WithErrors(installResult);
            }

            var pythonFolder = installResult.Value;

            // Get uv exe path (Windows/macOS/Linux)
            var uvFileName = OperatingSystem.IsWindows() ? UVExecutableNameWindows : UVExecutableName;
            var uvExePath = Path.Combine(pythonFolder, uvFileName);
            var uvExeInfoResult = await _fileSystem.GetInfoAsync(uvExePath);
            bool uvExeExists = uvExeInfoResult.IsSuccess
                && uvExeInfoResult.Value.Kind == StorageItemKind.File;
            if (!uvExeExists)
            {
                var errorMessage = new ConsoleErrorMessage(
                    ConsoleErrorType.PythonHostPreInitError,
                    $"uv not found at '{uvExePath}'");
                _messengerService.Send(errorMessage);
                return Result.Fail($"uv not found at '{uvExePath}'");
            }

            // uv's package cache and interpreter installs live under the per-project Python
            // folder, not the shared app folder, so each project owns its own environment.
            var uvCacheDir = Path.Combine(projectPythonFolder, UVCacheFolderName);

            var uvPythonInstallDir = Path.Combine(projectPythonFolder, UVPythonInstallsFolderName);
            await _fileSystem.CreateFolderAsync(uvPythonInstallDir);

            // Prepare the per-process environment variables for the terminal.
            // These are injected into the child process environment block rather than set
            // process-wide, so multiple terminals can have different configurations.
            var ipythonDir = Path.Combine(projectPythonFolder, IPythonCacheFolderName);
            await _fileSystem.CreateFolderAsync(ipythonDir);

            var configuration = environmentInfo.Configuration;
            var celbridgeVersion = configuration == "Debug" ? $"{appVersion} (Debug)" : $"{appVersion}";

            var pythonLogFolder = Path.Combine(workingDir, ProjectConstants.CelbridgeFolder, ProjectConstants.LogsFolder);

            // Find a free TCP port for JSON-RPC communication
            var rpcPort = GetAvailableTcpPort();
            _logger.LogInformation("Selected RPC TCP port: {Port}", rpcPort);

            // Build the per-process environment for the terminal
            var uvToolsFolder = Path.Combine(projectPythonFolder, UVToolsFolderName);
            var uvBinFolder = Path.Combine(projectPythonFolder, UVBinFolderName);
            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            var terminalPath = currentPath.Contains(uvBinFolder, StringComparison.OrdinalIgnoreCase)
                ? currentPath
                : uvBinFolder + Path.PathSeparator + currentPath;

            var terminalEnvironment = new Dictionary<string, string>
            {
                ["CELBRIDGE_RPC_PORT"] = rpcPort.ToString(),
                ["CELBRIDGE_MCP_PORT"] = _serverService.Port.ToString(),
                ["CELBRIDGE_MCP_TOOLS"] = _featureFlags.IsEnabled(FeatureFlagConstants.McpTools) ? "1" : "0",
                ["CELBRIDGE_WEB_ACCESS_TOOLS"] = _featureFlags.IsEnabled(FeatureFlagConstants.WebAccessTools) ? "1" : "0",
                ["CELBRIDGE_PROJECT_FOLDER"] = project.ProjectFolderPath,
                ["CELBRIDGE_VERSION"] = celbridgeVersion,
                ["CELBRIDGE_IPYTHON_DIR"] = ipythonDir,
                ["PYTHON_LOG_LEVEL"] = "DEBUG",
                ["PYTHON_LOG_DIR"] = pythonLogFolder,
                ["PYTHON_LOG_MAX_FILES"] = PythonLogMaxFiles.ToString(),
                ["UV_PYTHON_INSTALL_DIR"] = uvPythonInstallDir,
                ["PATH"] = terminalPath
            };

            // Get the path to the celbridge wheel file
            var findCelbridgeWheelResult = await FindWheelFileAsync(pythonFolder, "celbridge");
            if (findCelbridgeWheelResult.IsFailure)
            {
                return Result.Fail("Failed to find celbridge wheel file")
                    .WithErrors(findCelbridgeWheelResult);
            }
            var celbridgeWheelPath = findCelbridgeWheelResult.Value;

            // The celbridge wheel includes ipython as a dependency, so no separate --with is needed
            var packageArgs = new List<string>();

            // Add any additional packages specified in the project config
            var pythonPackages = pythonConfig.Dependencies;
            if (pythonPackages is not null)
            {
                foreach (var pythonPackage in pythonPackages)
                {
                    packageArgs.Add("--with");
                    packageArgs.Add(pythonPackage);
                }
            }

            // Determine if we can use offline mode (no network required).
            // The fingerprint combines the config, a hash of the wheel file contents, and a
            // structural hash of the stable parts of the install: the shared uv binary plus
            // this project's interpreter set and wheel cache. Volatile folders that uv writes
            // to during normal operation (the rest of uv_cache, uv_tools, uv_bin, and
            // per-interpreter __pycache__) are deliberately excluded so the hash is stable
            // across sessions.
            var wheelHash = await FileHashHelper.HashFileContentsAsync(celbridgeWheelPath);
            var installStateHash = await ComputeInstallStateHashAsync(pythonFolder, projectPythonFolder);
            var currentFingerprint = ComputeConfigFingerprint(appVersion, pythonVersion!, celbridgeWheelPath, wheelHash, pythonPackages, installStateHash);
            var useOfflineMode = currentFingerprint == savedFingerprint;

            // Fingerprint values are logged at debug level so diagnosis of
            // unexpected offline/online transitions does not require new code,
            // while normal operation does not spam the info log.
            _logger.LogDebug(
                "Python fingerprint: wheelHash='{WheelHash}' installStateHash='{InstallStateHash}' current='{Current}' saved='{Saved}' useOfflineMode={Offline}",
                wheelHash,
                installStateHash,
                currentFingerprint,
                savedFingerprint ?? "<null>",
                useOfflineMode);

            if (useOfflineMode)
            {
                _logger.LogInformation("Python config unchanged since last run, using offline mode");
            }
            else
            {
                // Online mode lets uv reconcile this project's cache incrementally, fetching
                // only what is missing. We no longer wipe the cache on a fingerprint miss:
                // the cache is per-project, so there is no shared state to recover from.
                var reason = savedFingerprint is null ? "no saved fingerprint (first run)" : "config changed since last run";
                _logger.LogInformation("Using online mode for Python: {Reason}", reason);
            }

            // Install the celbridge package as a uv tool so the 'celbridge' command is
            // available on PATH for the user to type in the terminal after exiting the REPL.
            var uvBinFolderInfo = await _fileSystem.GetInfoAsync(uvBinFolder);
            bool uvBinFolderExists = uvBinFolderInfo.IsSuccess
                && uvBinFolderInfo.Value.Kind == StorageItemKind.Folder;
            var shouldInstallTool = !useOfflineMode || !uvBinFolderExists;
            if (shouldInstallTool)
            {
                await InstallCelbridgeToolAsync(
                    uvExePath, uvCacheDir, uvToolsFolder, uvBinFolder,
                    uvPythonInstallDir, pythonVersion!, celbridgeWheelPath);
            }

            // Build the inner command that launches the celbridge Python connector
            var uvBuilder = new CommandLineBuilder(uvExePath)
                .Add("run")
                .Add("--cache-dir", uvCacheDir);

            if (useOfflineMode)
            {
                uvBuilder.Add("--offline");
            }

            var uvCommand = uvBuilder
                .Add("--no-project")
                .Add("--python", pythonVersion!)
                .Add("--managed-python")
                .Add("--with", celbridgeWheelPath)
                .Add(packageArgs.ToArray())
                .Add("python")
                .Add("-m", "celbridge")
                .ToString();

            // Keep the terminal alive after the REPL exits so the user can type 'celbridge-py' to start a
            // new session. On Windows the ConPty backend runs the command line via cmd.exe. The Unix
            // backend already runs the command line through /bin/sh -c, so the uv command is appended with
            // 'exec bash' rather than wrapped in another shell (a second 'bash -c' would collide with the
            // single quotes uvCommand already uses to quote its own arguments).
            var commandLine = OperatingSystem.IsWindows()
                ? $"cmd.exe /k \"{uvCommand}\""
                : $"{uvCommand}; exec bash";

            // Cancel any previous RPC listening loop in case InitializePython
            // is called again after a project reload.
            _rpcCancellationTokenSource?.Cancel();
            _rpcCancellationTokenSource?.Dispose();

            // Unsubscribe previous handlers to prevent accumulation on reload
            _tcpTransport.ConnectionAccepted -= OnConnectionAccepted;
            _tcpTransport.ConnectionLost -= OnConnectionLost;

            // Reset connection tracking state for this initialization
            _pendingFingerprint = currentFingerprint;
            _pendingProjectPythonFolder = projectPythonFolder;
            _fingerprintSaved = false;
            _hadConnection = false;

            // Subscribe to connection events to track Python host availability
            _tcpTransport.ConnectionAccepted += OnConnectionAccepted;
            _tcpTransport.ConnectionLost += OnConnectionLost;

            // Start the RPC accept loop in the background
            _rpcCancellationTokenSource = new CancellationTokenSource();
            _ = Task.Run(() => _tcpTransport.StartListeningAsync(rpcPort, _rpcCancellationTokenSource.Token));

            var terminal = _workspaceWrapper.WorkspaceService.ConsoleService.Terminal;

            terminal.ProcessExited += (sender, eventArgs) =>
            {
                if (!_hadConnection)
                {
                    _logger.LogError("Python process exited before establishing an RPC connection");
                    var projectFile = Path.GetFileName(project.ProjectFilePath);
                    var errorMessage = new ConsoleErrorMessage(ConsoleErrorType.PythonHostProcessError, projectFile);
                    _messengerService.Send(errorMessage);
                }
            };

            // Snapshot of the uv-managed Python install dir and the full uv command
            // at launch time. Logged at debug level so a "Python REPL won't start"
            // report can be correlated with what was actually on disk and what was
            // passed to uv, without spamming the info log on every load.
            try
            {
                var installDirInfo = await _fileSystem.GetInfoAsync(uvPythonInstallDir);
                bool installDirExists = installDirInfo.IsSuccess
                    && installDirInfo.Value.Kind == StorageItemKind.Folder;
                if (installDirExists)
                {
                    var enumerateFoldersResult = await _fileSystem.EnumerateAsync(uvPythonInstallDir, "*", recursive: false);
                    var installEntries = enumerateFoldersResult.Value
                        .Where(entry => entry.IsFolder)
                        .Select(entry => Path.GetFileName(entry.FullPath))
                        .ToList();
                    _logger.LogDebug(
                        "uv_python_installs ('{Path}') contains [{Entries}]",
                        uvPythonInstallDir,
                        string.Join(", ", installEntries));
                }
                else
                {
                    _logger.LogDebug("uv_python_installs ('{Path}') does not exist at launch", uvPythonInstallDir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enumerate uv_python_installs at '{Path}'", uvPythonInstallDir);
            }

            _logger.LogDebug(
                "Launching uv: UV_PYTHON_INSTALL_DIR='{InstallDir}' offlineMode={Offline} uvCommand={UvCommand}",
                uvPythonInstallDir,
                useOfflineMode,
                uvCommand);

            // Start the terminal process with per-process environment variables
            terminal.Start(commandLine, workingDir, terminalEnvironment);
            _logger.LogInformation("Python terminal started successfully");

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("An error occurred when initializing Python")
                         .WithException(ex);
        }
    }

    private void OnConnectionAccepted(int connectionId)
    {
        _logger.LogInformation("Python RPC connection {ConnectionId} established", connectionId);
        _hadConnection = true;

        // Save the fingerprint on the first successful connection so subsequent
        // runs can use offline mode. Block on the async save here because this
        // event handler is sync and the operation is small and non-critical.
        if (!_fingerprintSaved)
        {
            SaveFingerprintAsync(_pendingProjectPythonFolder, _pendingFingerprint).GetAwaiter().GetResult();
            _fingerprintSaved = true;
        }

        IsPythonHostAvailable = true;
        _messengerService.Send(new PythonHostInitializedMessage());
    }

    private void OnConnectionLost(int connectionId)
    {
        _logger.LogInformation("Python RPC connection {ConnectionId} lost", connectionId);
        IsPythonHostAvailable = _tcpTransport.ActiveConnectionCount > 0;
    }

    /// <summary>
    /// Installs the celbridge package as a uv tool so the 'celbridge' command is
    /// available on PATH for manual invocation in the terminal.
    /// </summary>
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

        // Pass arguments through ArgumentList (raw, unquoted) rather than a single Arguments string:
        // ProcessStartInfo quotes each element correctly for the platform. A shell-quoted string would
        // be taken literally here, since the argument parser does not treat single quotes as shell
        // quotes (uv would receive a literal 'tool' and reject it).
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

        // uv tool install uses UV_TOOL_DIR and UV_TOOL_BIN_DIR environment variables
        // (not CLI flags) to control where tools are installed.
        processStartInfo.Environment["UV_TOOL_DIR"] = uvToolsFolder;
        processStartInfo.Environment["UV_TOOL_BIN_DIR"] = uvBinFolder;
        processStartInfo.Environment["UV_PYTHON_INSTALL_DIR"] = uvPythonInstallDir;

        using var process = Process.Start(processStartInfo);
        if (process != null)
        {
            // Read stdout and stderr before waiting to avoid deadlocks
            // when the output buffer fills up
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            // Timeout after 2 minutes to avoid hanging indefinitely if uv
            // stalls on network issues or DNS resolution.
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

    /// <summary>
    /// Finds an available TCP port by binding to port 0 and reading the assigned port.
    /// </summary>
    private static int GetAvailableTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Computes a hash of the stable parts of the install: the shared uv binary plus this
    /// project's interpreter set and wheel cache. Mismatch against a previously-saved value
    /// indicates the install has drifted and offline mode is unsafe.
    /// </summary>
    private async Task<string> ComputeInstallStateHashAsync(string appPythonFolder, string projectPythonFolder)
    {
        var sb = new StringBuilder();

        // uv binary file size catches an app update that bundles a new uv. The binary is
        // shared at the application level. The interpreter and cache below are per-project.
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

        // Depth 1 over uv_python_installs/ enumerates the interpreter folder names
        // (e.g. cpython-3.13.6-windows-x86_64-none) without descending into Lib/
        // where __pycache__ writes would destabilise the hash.
        var installsHash = await FileHashHelper.HashFolderStructureAsync(
            Path.Combine(projectPythonFolder, UVPythonInstallsFolderName),
            maxDepth: 1);
        sb.AppendLine($"installs|{installsHash}");

        // Wheel cache lives under uv_cache/wheels-v<N>/, where the version suffix
        // bumps with uv releases — hash each wheels-v* separately so a suffix
        // change is also captured. Depth 3 reaches package-name granularity
        // (wheels-v5/index/<src>/<pkg>/) without descending into per-version
        // wheels, keeping the hash stable when uv touches deeper cache metadata.
        // Other uv_cache subfolders (environments-v*, sdists-*, etc.) and the
        // regenerated uv_tools / uv_bin are deliberately excluded as volatile.
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

    /// <summary>
    /// Computes a fingerprint of the current Python configuration combined with a
    /// stable-state hash of the AppData Python folder. Any change to the app version,
    /// wheel content, dependencies, or the installed interpreter set causes a
    /// fingerprint mismatch that forces online mode.
    /// </summary>
    private static string ComputeConfigFingerprint(
        string appVersion,
        string pythonVersion,
        string celbridgeWheelPath,
        string wheelHash,
        IReadOnlyList<string>? dependencies,
        string installStateHash)
    {
        var sb = new StringBuilder();
        sb.AppendLine(appVersion);
        sb.AppendLine(pythonVersion);
        sb.AppendLine(Path.GetFileName(celbridgeWheelPath));
        sb.AppendLine(wheelHash);

        if (dependencies is not null)
        {
            foreach (var dep in dependencies)
            {
                sb.AppendLine(dep);
            }
        }

        sb.AppendLine(installStateHash);

        return FileHashHelper.HashString(sb.ToString());
    }

    /// <summary>
    /// Loads the previously saved config fingerprint from the project Python folder.
    /// Returns null if no fingerprint file exists.
    /// </summary>
    private async Task<string?> LoadSavedFingerprintAsync(string projectPythonFolder)
    {
        var filePath = Path.Combine(projectPythonFolder, PythonFingerprintFileName);
        var fingerprintInfoResult = await _fileSystem.GetInfoAsync(filePath);
        bool fingerprintExists = fingerprintInfoResult.IsSuccess
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

        var fingerprintText = readResult.Value;
        return fingerprintText.Trim();
    }

    /// <summary>
    /// Saves the current config fingerprint to the project Python folder.
    /// </summary>
    private async Task SaveFingerprintAsync(string projectPythonFolder, string fingerprint)
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

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _rpcCancellationTokenSource?.Cancel();
                _rpcCancellationTokenSource?.Dispose();
                _rpcCancellationTokenSource = null;

                _tcpTransport.Dispose();
            }

            _disposed = true;
        }
    }

    ~PythonService()
    {
        Dispose(false);
    }
}
