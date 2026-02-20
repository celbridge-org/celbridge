using System.Security.Cryptography;
using System.Text;
using Celbridge.ApplicationEnvironment;
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

    // Environment variable names
    private const string UVPythonInstallDirEnv = "UV_PYTHON_INSTALL_DIR";
    private const string UVToolDirEnv = "UV_TOOL_DIR";
    private const string UVToolBinDirEnv = "UV_TOOL_BIN_DIR";
    private const string CelbridgeVersionEnv = "CELBRIDGE_VERSION";
    private const string PythonLogLevelEnv = "PYTHON_LOG_LEVEL";
    private const string PythonLogDirEnv = "PYTHON_LOG_DIR";
    private const string PythonLogMaxFilesEnv = "PYTHON_LOG_MAX_FILES";
    private const string CelbridgeRpcPipeEnv = "CELBRIDGE_RPC_PIPE";
    private const string CelbridgeUVPathEnv = "CELBRIDGE_UV_PATH";
    private const string CelbridgeUVCacheDirEnv = "CELBRIDGE_UV_CACHE_DIR";
    private const string CelbridgePackagePathEnv = "CELBRIDGE_PACKAGE_PATH";

    private readonly IProjectService _projectService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IEnvironmentService _environmentService;
    private readonly IMessengerService _messengerService;
    private readonly ILogger<PythonService> _logger;
    private readonly Func<string, IRpcService> _rpcServiceFactory;
    private readonly Func<IRpcService, IPythonRpcClient> _pythonRpcClientFactory;

    private IRpcService? _rpcService;
    private IPythonRpcClient? _pythonRpcClient;

    public IPythonRpcClient RpcClient => _pythonRpcClient!;

    public bool IsPythonHostAvailable { get; private set; } = false;

    public PythonService(
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper,
        IEnvironmentService environmentService,
        IMessengerService messengerService,
        ILogger<PythonService> logger,
        Func<string, IRpcService> rpcServiceFactory,
        Func<IRpcService, IPythonRpcClient> pythonRpcClientFactory)
    {
        _projectService = projectService;
        _workspaceWrapper = workspaceWrapper;
        _environmentService = environmentService;
        _messengerService = messengerService;
        _logger = logger;
        _rpcServiceFactory = rpcServiceFactory;
        _pythonRpcClientFactory = pythonRpcClientFactory;
    }

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
            var pythonConfig = project.ProjectConfig?.Config?.Project!;
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

            var appVersion = _environmentService.GetEnvironmentInfo().AppVersion;
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
            Environment.SetEnvironmentVariable(UVPythonInstallDirEnv, uvPythonInstallDir);

            // Set UV_TOOL_DIR and UV_TOOL_BIN_DIR so the REPL process can access the installed celbridge tool
            // e.g. !celbridge help
            var uvToolDir = Path.Combine(pythonFolder, UVToolsFolderName);
            var uvToolBinDir = Path.Combine(pythonFolder, UVBinFolderName);
            Environment.SetEnvironmentVariable(UVToolDirEnv, uvToolDir);
            Environment.SetEnvironmentVariable(UVToolBinDirEnv, uvToolBinDir);

            // Ensure the ipython storage dir exists
            var ipythonDir = Path.Combine(workingDir, ProjectConstants.MetaDataFolder, ProjectConstants.CacheFolder, IPythonCacheFolderName);
            Directory.CreateDirectory(ipythonDir);

            // Set the Celbridge version number as an environment variable so we can print it at startup.
            var environmentInfo = _environmentService.GetEnvironmentInfo();
            var version = environmentInfo.AppVersion;
            var configuration = environmentInfo.Configuration;
            var celbridgeVersion = configuration == "Debug" ? $"{version} (Debug)" : $"{version}";
            Environment.SetEnvironmentVariable(CelbridgeVersionEnv, celbridgeVersion);

            // Set Python logging environment variables
            Environment.SetEnvironmentVariable(PythonLogLevelEnv, "DEBUG");
            var pythonLogFolder = Path.Combine(workingDir, ProjectConstants.MetaDataFolder, ProjectConstants.LogsFolder);
            Environment.SetEnvironmentVariable(PythonLogDirEnv, pythonLogFolder);
            Environment.SetEnvironmentVariable(PythonLogMaxFilesEnv, PythonLogMaxFiles.ToString());

            // Generate unique pipe name for JSON-RPC communication
            var pipeName = $"celbridge_rpc_{Guid.NewGuid():N}";
            Environment.SetEnvironmentVariable(CelbridgeRpcPipeEnv, pipeName);
            _logger.LogInformation("Generated RPC pipe name: {PipeName}", pipeName);

            // Get the path to the celbridge wheel file
            // This is the CLI application that implements the core functionality of Celbridge.
            var findCelbridgeWheelResult = FindWheelFile(pythonFolder, "celbridge");
            if (findCelbridgeWheelResult.IsFailure)
            {
                return Result.Fail("Failed to find celbridge wheel file")
                    .WithErrors(findCelbridgeWheelResult);
            }
            var celbridgeWheelPath = findCelbridgeWheelResult.Value;

            // Set environment variables for celbridge_host to use uv with dependency isolation
            Environment.SetEnvironmentVariable(CelbridgeUVPathEnv, uvExePath);
            Environment.SetEnvironmentVariable(CelbridgeUVCacheDirEnv, uvCacheDir);
            Environment.SetEnvironmentVariable(CelbridgePackagePathEnv, celbridgeWheelPath);

            // Get the path to the celbridge_host wheel file
            var findHostWheelResult = FindWheelFile(pythonFolder, "celbridge_host");
            if (findHostWheelResult.IsFailure)
            {
                return Result.Fail("Failed to find celbridge_host wheel file")
                    .WithErrors(findHostWheelResult);
            }
            var hostWheelPath = findHostWheelResult.Value;

            // The celbridge_host and ipython packages are always included
            var packageArgs = new List<string>()
            {
                "--with", hostWheelPath,
                "--with", IPythonCacheFolderName
            };

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
            // If the python version, dependencies, and wheel files have not changed since the last
            // successful startup, we can use --offline to avoid network access.
            var cacheDir = Path.Combine(workingDir, ProjectConstants.MetaDataFolder, ProjectConstants.CacheFolder);
            var currentFingerprint = ComputeConfigFingerprint(appVersion, pythonVersion!, hostWheelPath, celbridgeWheelPath, pythonPackages);
            var savedFingerprint = LoadSavedFingerprint(cacheDir);
            var useOfflineMode = currentFingerprint == savedFingerprint;
            if (useOfflineMode)
            {
                _logger.LogInformation("Python config unchanged since last run, using offline mode");
            }

            // Run the celbridge module then drop to the IPython REPL
            // The order of the command line arguments is important!

            var builder = new CommandLineBuilder(uvExePath)
                .Add("run")                                 // uv run
                .Add("--cache-dir", uvCacheDir);             // cache uv files in app data folder (not globally per-user)

            if (useOfflineMode)
            {
                builder.Add("--offline");                   // use only locally cached packages (no network required)
            }

            var commandLine = builder
                .Add("--no-project")                        // ignore pyproject.toml file if present (dependencies are passed via --with instead)
                .Add("--python", pythonVersion!)            // python interpreter version
                .Add("--managed-python")                    // only use uv-managed Python, ignore system Python
                                                            //.Add("--refresh-package", "celbridge_host") // uncomment to always refresh the celbridge_host package
                .Add(packageArgs.ToArray())                 // specify the packages to install     
                .Add("python")                              // run the python interpreter
                .Add("-m", "IPython")                       // use IPython
                .Add("--no-banner")                         // don't show the IPython banner
                .Add("--ipython-dir", ipythonDir)           // use a ipython storage dir in the celbridge cache folder
                .Add("-m", "celbridge_host")                // run the celbridge module
                .Add("-i")                                  // drop to interactive mode after running celbridge module
                .ToString();

            // Save the current fingerprint so subsequent runs can use offline mode
            SaveFingerprint(cacheDir, currentFingerprint);

            var terminal = _workspaceWrapper.WorkspaceService.ConsoleService.Terminal;

            // Start the terminal process
            // Any errors during Python/uv initialization will be displayed in the terminal
            terminal.Start(commandLine, workingDir);
            _logger.LogInformation("Python terminal started successfully");

            // Create RPC service (but don't connect yet)
            _rpcService = _rpcServiceFactory(pipeName);

            // Connect to RPC in the background without blocking workspace loading
            // The Python process needs time to start up and create the RPC server
            _ = Task.Run(async () =>
            {
                try
                {
                    // Wait for Python process to start RPC server (with timeout)
                    var connectResult = await _rpcService.ConnectAsync();
                    if (connectResult.IsFailure)
                    {
                        _logger.LogWarning("Failed to connect to Python RPC server: {Error}", connectResult.Error);
                        _logger.LogWarning("Python REPL is available, but RPC features may not work until the connection is established.");
                        return;
                    }

                    // Create Python RPC client for strongly-typed Python method calls
                    _pythonRpcClient = _pythonRpcClientFactory(_rpcService);
                    _logger.LogInformation("Python RPC client connected successfully");

                    IsPythonHostAvailable = true;

                    var message = new PythonHostInitializedMessage();
                    _messengerService.Send(message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception occurred while connecting to Python RPC server");
                }
            });

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("An error occurred when initializing Python")
                         .WithException(ex);
        }
    }

    /// <summary>
    /// Computes a fingerprint of the current Python configuration.
    /// If this fingerprint matches the saved one, we can use offline mode.
    /// </summary>
    private static string ComputeConfigFingerprint(
        string appVersion,
        string pythonVersion,
        string hostWheelPath,
        string celbridgeWheelPath,
        IReadOnlyList<string>? dependencies)
    {
        var sb = new StringBuilder();
        sb.AppendLine(appVersion);
        sb.AppendLine(pythonVersion);
        sb.AppendLine(Path.GetFileName(hostWheelPath));
        sb.AppendLine(Path.GetFileName(celbridgeWheelPath));

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
    /// Finds the latest version of a wheel file for the specified package in the given folder.
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

            // Parse version numbers and find the latest
            var wheelVersions = wheelFiles
                .Select(filePath =>
                {
                    var fileName = Path.GetFileName(filePath);
                    Version? version = null;

                    // Extract version from wheel filename (format: {packageName}-1.2.3-py3-none-any.whl)
                    try
                    {
                        var parts = fileName.Split('-');
                        if (parts.Length >= 2 && parts[0] == packageName)
                        {
                            var versionString = parts[1];
                            Version.TryParse(versionString, out version);
                        }
                    }
                    catch
                    {
                        // Ignore parsing errors
                    }

                    return new
                    {
                        Path = filePath,
                        FileName = fileName,
                        Version = version
                    };
                })
                .Where(x => x.Version != null)
                .OrderByDescending(x => x.Version)
                .ToList();

            if (wheelVersions.Count == 0)
            {
                return Result<string>.Fail($"No valid versioned wheel files found for package '{packageName}'");
            }

            return Result<string>.Ok(wheelVersions.First().Path);
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
                // Dispose RPC service
                _rpcService?.Dispose();
                _rpcService = null;
            }

            _disposed = true;
        }
    }

    ~PythonService()
    {
        Dispose(false);
    }
}
