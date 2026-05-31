using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using Celbridge.ApplicationEnvironment;
using Celbridge.Console;
using Celbridge.FileSystem;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Resources;
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
    private const string InstalledVersionFileName = "installed_version.txt";

    private readonly IProjectService _projectService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IEnvironmentService _environmentService;
    private readonly IServerService _serverService;
    private readonly IMessengerService _messengerService;
    private readonly IFeatureFlags _featureFlags;
    private readonly IPythonInstaller _pythonInstaller;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<PythonService> _logger;
    private readonly ITcpTransport _tcpTransport;
    private CancellationTokenSource? _rpcCancellationTokenSource;
    private string _pendingFingerprint = string.Empty;
    private string _pendingCacheDir = string.Empty;
    private bool _fingerprintSaved;
    private volatile bool _hadConnection;

    public PythonService(
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper,
        IEnvironmentService environmentService,
        IServerService serverService,
        IMessengerService messengerService,
        IFeatureFlags featureFlags,
        IPythonInstaller pythonInstaller,
        IFileSystem fileSystem,
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

    [SupportedOSPlatform("windows10.0.10240.0")]
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

            // Load the saved fingerprint once for use in both the pre-install check
            // and the offline mode comparison later.
            var cacheDir = Path.Combine(workingDir, ProjectConstants.CelbridgeFolder, ProjectConstants.PythonFolder);
            var savedFingerprint = await LoadSavedFingerprintAsync(cacheDir);

            // If no fingerprint file exists, delete the installer's version marker BEFORE
            // the installer runs. This ensures the installer re-extracts assets (including
            // the wheel) even if the app version hasn't changed.
            if (savedFingerprint is null)
            {
                _logger.LogInformation("No Python fingerprint found, will force full reinstall");
                var pythonFolderForCleanup = Path.Combine(
                    ApplicationData.Current.LocalFolder.Path, "Python");
                await DeleteInstalledVersionMarkerAsync(pythonFolderForCleanup);
            }

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

            // Get the dir that uv uses to cached python versions & packages
            var uvCacheDir = Path.Combine(pythonFolder, UVCacheFolderName);

            // Set where uv installs Python interpreters
            var uvPythonInstallDir = Path.Combine(pythonFolder, UVPythonInstallsFolderName);
            await _fileSystem.CreateFolderAsync(uvPythonInstallDir);

            // Prepare the per-process environment variables for the terminal.
            // These are injected into the child process environment block rather than set
            // process-wide, so multiple terminals can have different configurations.
            var ipythonDir = Path.Combine(workingDir, ProjectConstants.CelbridgeFolder, ProjectConstants.PythonFolder, IPythonCacheFolderName);
            await _fileSystem.CreateFolderAsync(ipythonDir);

            var configuration = environmentInfo.Configuration;
            var celbridgeVersion = configuration == "Debug" ? $"{appVersion} (Debug)" : $"{appVersion}";

            var pythonLogFolder = Path.Combine(workingDir, ProjectConstants.CelbridgeFolder, ProjectConstants.LogsFolder);

            // Find a free TCP port for JSON-RPC communication
            var rpcPort = GetAvailableTcpPort();
            _logger.LogInformation("Selected RPC TCP port: {Port}", rpcPort);

            // Build the per-process environment for the terminal
            var uvToolsFolder = Path.Combine(pythonFolder, UVToolsFolderName);
            var uvBinFolder = Path.Combine(pythonFolder, UVBinFolderName);
            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            var terminalPath = currentPath.Contains(uvBinFolder, StringComparison.OrdinalIgnoreCase)
                ? currentPath
                : uvBinFolder + Path.PathSeparator + currentPath;

            var terminalEnvironment = new Dictionary<string, string>
            {
                ["CELBRIDGE_RPC_PORT"] = rpcPort.ToString(),
                ["CELBRIDGE_MCP_PORT"] = _serverService.Port.ToString(),
                ["CELBRIDGE_MCP_TOOLS"] = _featureFlags.IsEnabled(FeatureFlagConstants.McpTools) ? "1" : "0",
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
            // The fingerprint includes the config, a hash of the wheel file contents,
            // and a structural hash of the stable parts of the AppData Python folder
            // (uv binary + installed Python interpreter set). Volatile folders that
            // uv writes to during normal operation (uv_cache, uv_tools, uv_bin, and
            // per-interpreter __pycache__) are deliberately excluded so the hash is
            // stable across sessions.
            var wheelHash = FileHashHelper.HashFileContents(celbridgeWheelPath);
            var installStateHash = await ComputeInstallStateHashAsync(pythonFolder);
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
            else if (savedFingerprint is null)
            {
                // No fingerprint file found. Wipe the uv cache and tool folders to force
                // a full reinstall of the Python interpreter, packages, and tools.
                // The installed version marker was already deleted before the installer ran.
                _logger.LogInformation("No Python fingerprint found, clearing uv cache for full reinstall");
                await ClearUvCacheAsync(uvCacheDir);
                await ClearUvCacheAsync(uvToolsFolder);
                await ClearUvCacheAsync(uvBinFolder);
            }
            else
            {
                _logger.LogInformation("Python config changed since last run, using online mode");
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
                    pythonVersion!, celbridgeWheelPath);
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

            // Wrap in a shell so the terminal stays alive after the REPL exits.
            // This lets the user type 'celbridge-py' to start a new session.
            var commandLine = OperatingSystem.IsWindows()
                ? $"cmd.exe /k \"{uvCommand}\""
                : $"bash -c '{uvCommand}; exec bash'";

            // Cancel any previous RPC listening loop in case InitializePython
            // is called again after a project reload.
            _rpcCancellationTokenSource?.Cancel();
            _rpcCancellationTokenSource?.Dispose();

            // Unsubscribe previous handlers to prevent accumulation on reload
            _tcpTransport.ConnectionAccepted -= OnConnectionAccepted;
            _tcpTransport.ConnectionLost -= OnConnectionLost;

            // Reset connection tracking state for this initialization
            _pendingFingerprint = currentFingerprint;
            _pendingCacheDir = cacheDir;
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
                    var enumerateFoldersResult = await _fileSystem.EnumerateFoldersAsync(uvPythonInstallDir);
                    var installEntries = enumerateFoldersResult.Value
                        .Select(folder => Path.GetFileName(folder))
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
            SaveFingerprintAsync(_pendingCacheDir, _pendingFingerprint).GetAwaiter().GetResult();
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
    [SupportedOSPlatform("windows10.0.10240.0")]
    private async Task InstallCelbridgeToolAsync(
        string uvExePath,
        string uvCacheDir,
        string uvToolsFolder,
        string uvBinFolder,
        string pythonVersion,
        string celbridgeWheelPath)
    {
        _logger.LogInformation("Installing celbridge as uv tool");

        // Build the arguments list separately from the executable path.
        // ProcessStartInfo takes FileName and Arguments as separate fields,
        // so we don't use CommandLineBuilder here (which combines them).
        var toolInstallArguments = new CommandLineBuilder("tool")
            .Add("install")
            .Add("--force")
            .Add("--cache-dir", uvCacheDir)
            .Add("--python", pythonVersion)
            .Add("--managed-python")
            .Add(celbridgeWheelPath)
            .ToString();

        _logger.LogDebug("uv tool install command: {FileName} {Arguments}", uvExePath, toolInstallArguments);

        // uv tool install uses UV_TOOL_DIR and UV_TOOL_BIN_DIR environment variables
        // (not CLI flags) to control where tools are installed.
        var processStartInfo = new ProcessStartInfo
        {
            FileName = uvExePath,
            Arguments = toolInstallArguments,
            WorkingDirectory = Path.GetDirectoryName(uvExePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        processStartInfo.Environment["UV_TOOL_DIR"] = uvToolsFolder;
        processStartInfo.Environment["UV_TOOL_BIN_DIR"] = uvBinFolder;
        processStartInfo.Environment["UV_PYTHON_INSTALL_DIR"] =
            Path.Combine(Path.GetDirectoryName(uvExePath)!, UVPythonInstallsFolderName);

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
    /// Computes a hash of the stable parts of the AppData Python folder. Mismatch
    /// against a previously-saved value indicates the install has drifted and
    /// offline mode is unsafe.
    /// </summary>
    private async Task<string> ComputeInstallStateHashAsync(string pythonFolder)
    {
        var sb = new StringBuilder();

        // uv binary file size catches an app update that bundles a new uv.
        var uvExeName = OperatingSystem.IsWindows() ? UVExecutableNameWindows : UVExecutableName;
        var uvExePath = Path.Combine(pythonFolder, uvExeName);
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
        var installsHash = FileHashHelper.HashFolderStructure(
            Path.Combine(pythonFolder, UVPythonInstallsFolderName),
            maxDepth: 1);
        sb.AppendLine($"installs|{installsHash}");

        // Wheel cache lives under uv_cache/wheels-v<N>/, where the version suffix
        // bumps with uv releases — hash each wheels-v* separately so a suffix
        // change is also captured. Depth 3 reaches package-name granularity
        // (wheels-v5/index/<src>/<pkg>/) without descending into per-version
        // wheels, keeping the hash stable when uv touches deeper cache metadata.
        // Other uv_cache subfolders (environments-v*, sdists-*, etc.) and the
        // regenerated uv_tools / uv_bin are deliberately excluded as volatile.
        var uvCacheDir = Path.Combine(pythonFolder, UVCacheFolderName);
        var uvCacheInfoResult = await _fileSystem.GetInfoAsync(uvCacheDir);
        if (uvCacheInfoResult.IsSuccess
            && uvCacheInfoResult.Value.Kind == StorageItemKind.Folder)
        {
            var wheelsFolders = new List<string>();
            var enumerateFoldersResult = await _fileSystem.EnumerateFoldersAsync(uvCacheDir);
            if (enumerateFoldersResult.IsSuccess)
            {
                foreach (var folder in enumerateFoldersResult.Value)
                {
                    var folderName = Path.GetFileName(folder);
                    if (folderName.StartsWith("wheels-v", StringComparison.Ordinal))
                    {
                        wheelsFolders.Add(folder);
                    }
                }
                wheelsFolders.Sort(StringComparer.Ordinal);
            }

            foreach (var wheelsFolder in wheelsFolders)
            {
                var folderName = Path.GetFileName(wheelsFolder);
                var wheelsHash = FileHashHelper.HashFolderStructure(wheelsFolder, maxDepth: 3);
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
    /// Loads the previously saved config fingerprint from the cache folder.
    /// Returns null if no fingerprint file exists.
    /// </summary>
    private async Task<string?> LoadSavedFingerprintAsync(string cacheDir)
    {
        var filePath = Path.Combine(cacheDir, PythonFingerprintFileName);
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
    /// Saves the current config fingerprint to the cache folder.
    /// </summary>
    private async Task SaveFingerprintAsync(string cacheDir, string fingerprint)
    {
        // Non-critical: failures here just mean the next run uses online mode.
        var createFolderResult = await _fileSystem.CreateFolderAsync(cacheDir);
        if (createFolderResult.IsFailure)
        {
            return;
        }

        var filePath = Path.Combine(cacheDir, PythonFingerprintFileName);
        await _fileSystem.WriteAllTextAsync(filePath, fingerprint);
    }

    /// <summary>
    /// Clears the uv package cache folder to force a full reinstall of the Python
    /// interpreter and all packages on the next uv run.
    /// </summary>
    private async Task ClearUvCacheAsync(string uvCacheFolder)
    {
        // Non-critical: if we can't clear the cache, uv still checks for
        // updates in online mode.
        var cacheInfoResult = await _fileSystem.GetInfoAsync(uvCacheFolder);
        if (cacheInfoResult.IsFailure
            || cacheInfoResult.Value.Kind != StorageItemKind.Folder)
        {
            return;
        }

        await _fileSystem.DeleteFolderAsync(uvCacheFolder, recursive: true);
    }

    /// <summary>
    /// Deletes the installed version marker file so that PythonInstaller treats
    /// the next run as a fresh install, re-extracting the wheel and uv assets.
    /// </summary>
    private async Task DeleteInstalledVersionMarkerAsync(string pythonFolder)
    {
        // Non-critical: PythonInstaller still checks the version content on failure.
        var markerPath = Path.Combine(pythonFolder, InstalledVersionFileName);
        var markerInfoResult = await _fileSystem.GetInfoAsync(markerPath);
        bool markerExists = markerInfoResult.IsSuccess
            && markerInfoResult.Value.Kind == StorageItemKind.File;
        if (!markerExists)
        {
            return;
        }

        await _fileSystem.DeleteFileAsync(markerPath);
    }

    /// <summary>
    /// Finds a wheel file for the specified package in the given folder.
    /// </summary>
    private async Task<Result<string>> FindWheelFileAsync(string folderPath, string packageName)
    {
        var searchPattern = $"{packageName}-*.whl";
        var enumerateFilesResult = await _fileSystem.EnumerateFilesAsync(folderPath, searchPattern, recursive: false);
        if (enumerateFilesResult.IsFailure)
        {
            return Result<string>.Fail($"Error searching for wheel files for package '{packageName}'")
                .WithErrors(enumerateFilesResult);
        }

        var wheelFiles = enumerateFilesResult.Value;
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
