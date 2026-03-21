using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Celbridge.ApplicationEnvironment;
using Celbridge.Broker;
using Celbridge.Console;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Workspace;
using Microsoft.Extensions.Logging;

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
    private const string BuildVersionFileName = "build_version.txt";
    private const string InstalledVersionFileName = "installed_version.txt";

    private readonly IProjectService _projectService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IEnvironmentService _environmentService;
    private readonly IMessengerService _messengerService;
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
        IMessengerService messengerService,
        ILogger<PythonService> logger,
        ITcpTransport tcpTransport)
    {
        _projectService = projectService;
        _workspaceWrapper = workspaceWrapper;
        _environmentService = environmentService;
        _messengerService = messengerService;
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
            var cacheDir = Path.Combine(workingDir, ProjectConstants.MetaDataFolder, ProjectConstants.CacheFolder);
            var savedFingerprint = LoadSavedFingerprint(cacheDir);

            // If no fingerprint file exists, delete the installer's version marker BEFORE
            // the installer runs. This ensures the installer re-extracts assets (including
            // the wheel) even if the app version hasn't changed.
            if (savedFingerprint is null)
            {
                _logger.LogInformation("No Python fingerprint found, will force full reinstall");
                var pythonFolderForCleanup = Path.Combine(
                    ApplicationData.Current.LocalFolder.Path, "Python");
                DeleteInstalledVersionMarker(pythonFolderForCleanup);
            }

            var installResult = await PythonInstaller.InstallPythonAsync(appVersion);
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
            if (!File.Exists(uvExePath))
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
            Directory.CreateDirectory(uvPythonInstallDir);

            // Prepare the per-process environment variables for the terminal.
            // These are injected into the child process environment block rather than set
            // process-wide, so multiple terminals can have different configurations.
            var ipythonDir = Path.Combine(workingDir, ProjectConstants.MetaDataFolder, ProjectConstants.CacheFolder, IPythonCacheFolderName);
            Directory.CreateDirectory(ipythonDir);

            var configuration = environmentInfo.Configuration;
            var celbridgeVersion = configuration == "Debug" ? $"{appVersion} (Debug)" : $"{appVersion}";

            var pythonLogFolder = Path.Combine(workingDir, ProjectConstants.MetaDataFolder, ProjectConstants.LogsFolder);

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
                ["CELBRIDGE_VERSION"] = celbridgeVersion,
                ["CELBRIDGE_IPYTHON_DIR"] = ipythonDir,
                ["PYTHON_LOG_LEVEL"] = "DEBUG",
                ["PYTHON_LOG_DIR"] = pythonLogFolder,
                ["PYTHON_LOG_MAX_FILES"] = PythonLogMaxFiles.ToString(),
                ["UV_PYTHON_INSTALL_DIR"] = uvPythonInstallDir,
                ["PATH"] = terminalPath
            };

            // Get the path to the celbridge wheel file
            var findCelbridgeWheelResult = FindWheelFile(pythonFolder, "celbridge");
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
            // The fingerprint includes the config, the build version GUID (changes when wheels
            // are rebuilt), AND the directory structure of the Python install folder on disk.
            // A change to any of these forces online mode to re-download everything.
            // The build version GUID changes whenever the Python wheel is rebuilt,
            // which is sufficient to detect when a reinstall is needed.
            var buildVersion = ReadBuildVersion(pythonFolder);
            var currentFingerprint = ComputeConfigFingerprint(appVersion, pythonVersion!, celbridgeWheelPath, buildVersion, pythonPackages);
            var useOfflineMode = currentFingerprint == savedFingerprint;

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
                ClearUvCache(uvCacheDir);
                ClearUvCache(uvToolsFolder);
                ClearUvCache(uvBinFolder);
            }
            else
            {
                _logger.LogInformation("Python config changed since last run, using online mode");
            }

            // Install the celbridge package as a uv tool so the 'celbridge' command is
            // available on PATH for the user to type in the terminal after exiting the REPL.
            var shouldInstallTool = !useOfflineMode || !Directory.Exists(uvBinFolder);
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

        // Save the fingerprint on the first successful connection.
        // This means subsequent runs can use offline mode.
        if (!_fingerprintSaved)
        {
            SaveFingerprint(_pendingCacheDir, _pendingFingerprint);
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
    /// Computes a fingerprint of the current Python configuration and installed state.
    /// Includes the build version GUID (which changes when wheels are rebuilt) and the
    /// directory structure of the Python install folder so that any change to the app
    /// version, wheel content, installed Python versions, cached packages, or tools
    /// causes a fingerprint mismatch that forces online mode.
    /// </summary>
    private static string ComputeConfigFingerprint(
        string appVersion,
        string pythonVersion,
        string celbridgeWheelPath,
        string buildVersion,
        IReadOnlyList<string>? dependencies)
    {
        var sb = new StringBuilder();
        sb.AppendLine(appVersion);
        sb.AppendLine(pythonVersion);
        sb.AppendLine(Path.GetFileName(celbridgeWheelPath));
        sb.AppendLine(buildVersion);

        if (dependencies is not null)
        {
            foreach (var dep in dependencies)
            {
                sb.AppendLine(dep);
            }
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Reads the build version GUID from the Python install folder.
    /// This GUID is regenerated each time the Python wheels are rebuilt by build.py.
    /// </summary>
    private static string ReadBuildVersion(string pythonFolder)
    {
        try
        {
            var buildVersionPath = Path.Combine(pythonFolder, BuildVersionFileName);
            if (File.Exists(buildVersionPath))
            {
                return File.ReadAllText(buildVersionPath).Trim();
            }
        }
        catch
        {
            // Non-critical: if we can't read the build version, the other
            // fingerprint components will still detect most changes.
        }

        return string.Empty;
    }

    /// <summary>
    /// Computes a fingerprint of the install folder by hashing the relative path, size,
    /// and last-write timestamp of every file. This detects any file being added, deleted,
    /// renamed, or modified without reading file contents.
    /// </summary>
    private static string ComputeDirectoryFingerprint(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            return string.Empty;
        }

        var entries = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories)
            .Select(filePath =>
            {
                var relativePath = Path.GetRelativePath(folderPath, filePath);
                var fileInfo = new FileInfo(filePath);
                return $"{relativePath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
            })
            .OrderBy(entry => entry, StringComparer.Ordinal);

        var combined = string.Join("\n", entries);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Loads the previously saved config fingerprint from the cache folder.
    /// Returns null if no fingerprint file exists.
    /// </summary>
    private static string? LoadSavedFingerprint(string cacheDir)
    {
        var filePath = Path.Combine(cacheDir, PythonFingerprintFileName);
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(filePath).Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Saves the current config fingerprint to the cache folder.
    /// </summary>
    private static void SaveFingerprint(string cacheDir, string fingerprint)
    {
        try
        {
            Directory.CreateDirectory(cacheDir);
            var filePath = Path.Combine(cacheDir, PythonFingerprintFileName);
            File.WriteAllText(filePath, fingerprint);
        }
        catch
        {
            // Non-critical: failing to save the fingerprint just means
            // the next run will use online mode.
        }
    }

    /// <summary>
    /// Clears the uv package cache folder to force a full reinstall of the Python
    /// interpreter and all packages on the next uv run.
    /// </summary>
    private static void ClearUvCache(string uvCacheFolder)
    {
        try
        {
            if (Directory.Exists(uvCacheFolder))
            {
                Directory.Delete(uvCacheFolder, recursive: true);
            }
        }
        catch
        {
            // Non-critical: if we can't clear the cache, uv will still
            // check for updates in online mode.
        }
    }

    /// <summary>
    /// Deletes the installed version marker file so that PythonInstaller treats
    /// the next run as a fresh install, re-extracting the wheel and uv assets.
    /// </summary>
    private static void DeleteInstalledVersionMarker(string pythonFolder)
    {
        try
        {
            var markerPath = Path.Combine(pythonFolder, InstalledVersionFileName);
            if (File.Exists(markerPath))
            {
                File.Delete(markerPath);
            }
        }
        catch
        {
            // Non-critical: PythonInstaller will still check the version content.
        }
    }

    /// <summary>
    /// Finds a wheel file for the specified package in the given folder.
    /// </summary>
    private static Result<string> FindWheelFile(string folderPath, string packageName)
    {
        try
        {
            var searchPattern = $"{packageName}-*.whl";
            var wheelFiles = Directory.GetFiles(folderPath, searchPattern, SearchOption.TopDirectoryOnly);

            if (wheelFiles.Length == 0)
            {
                return Result<string>.Fail($"No wheel files found for package '{packageName}' in '{folderPath}'");
            }

            return Result<string>.Ok(wheelFiles[0]);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Error searching for wheel files for package '{packageName}'")
                .WithException(ex);
        }
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
