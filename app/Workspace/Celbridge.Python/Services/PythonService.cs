using Celbridge.Projects;
using Celbridge.Utilities;
using Celbridge.Workspace;
using Microsoft.Extensions.Logging;

using Path = System.IO.Path;

namespace Celbridge.Python.Services;

public class PythonService : IPythonService, IDisposable
{
    private const int PythonLogMaxFiles = 10;

    // Folder and file names
    private const string UVCacheFolderName = "uv_cache";
    private const string UVExecutableName = "uv";
    private const string UVExecutableNameWindows = "uv.exe";
    private const string UVPythonInstallsFolderName = "uv_python_installs";
    private const string IPythonCacheFolderName = "ipython";

    // Environment variable names
    private const string UVPythonInstallDirEnv = "UV_PYTHON_INSTALL_DIR";
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
    private readonly IUtilityService _utilityService;
    private readonly ILogger<PythonService> _logger;
    private readonly Func<string, IRpcService> _rpcServiceFactory;
    private readonly Func<IRpcService, IPythonRpcClient> _pythonRpcClientFactory;

    private IRpcService? _rpcService;
    private IPythonRpcClient? _pythonRpcClient;

    public IPythonRpcClient RpcClient => _pythonRpcClient!;

    public PythonService(
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper,
        IUtilityService utilityService,
        ILogger<PythonService> logger,
        Func<string, IRpcService> rpcServiceFactory,
        Func<IRpcService, IPythonRpcClient> pythonRpcClientFactory)
    {
        _projectService = projectService;
        _workspaceWrapper = workspaceWrapper;
        _utilityService = utilityService;
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

            // Read python version from project config
            var pythonConfig = project.ProjectConfig?.Config?.Python!;
            if (pythonConfig is null)
            {
                return Result.Fail("Python section not specified in project config");
            }

            var pythonVersion = pythonConfig.Version;
            if (string.IsNullOrWhiteSpace(pythonVersion))
            {
                return Result.Fail("Python version not specified in project config");
            }

            // Ensure that python support files are installed

            var workingDir = project.ProjectFolderPath;

            var installResult = await PythonInstaller.InstallPythonAsync();
            if (installResult.IsFailure)
            {
                return Result.Fail("Failed to ensure Python support files are installed")
                    .WithErrors(installResult);
            }

            var pythonFolder = installResult.Value;

            // Get uv exe path (Windows/macOS/Linux)
            var uvFileName = OperatingSystem.IsWindows() ? UVExecutableNameWindows : UVExecutableName;
            var uvExePath = Path.Combine(pythonFolder, uvFileName);
            if (!File.Exists(uvExePath))
            {
                return Result.Fail($"uv not found at '{uvExePath}'");
            }

            // Get the dir that uv uses to cached python versions & packages
            var uvCacheDir = Path.Combine(pythonFolder, UVCacheFolderName);

            // Set where uv installs Python interpreters
            var uvPythonInstallDir = Path.Combine(pythonFolder, UVPythonInstallsFolderName);
            Directory.CreateDirectory(uvPythonInstallDir);
            Environment.SetEnvironmentVariable(UVPythonInstallDirEnv, uvPythonInstallDir);

            // Ensure the ipython storage dir exists
            var ipythonDir = Path.Combine(workingDir, ProjectConstants.MetaDataFolder, ProjectConstants.CacheFolder, IPythonCacheFolderName);
            Directory.CreateDirectory(ipythonDir);

            // Set the Celbridge version number as an environment variable so we can print it at startup.
            var environmentInfo = _utilityService.GetEnvironmentInfo();
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
                return findCelbridgeWheelResult;
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
                return findHostWheelResult;
            }
            var hostWheelPath = findHostWheelResult.Value;

            // The celbridge_host and ipython packages are always included
            var packageArgs = new List<string>()
            {
                "--with", hostWheelPath,
                "--with", IPythonCacheFolderName
            };
            
            // Add any additional packages specified in the project config
            var pythonPackages = pythonConfig.Packages;
            if (pythonPackages is not null)
            {
                foreach (var pythonPackage in pythonPackages)
                {
                    packageArgs.Add("--with");
                    packageArgs.Add(pythonPackage);    
                }
            }

            // Run the celbridge module then drop to the IPython REPL
            // The order of the command line arguments is important!

            var commandLine = new CommandLineBuilder(uvExePath)
                .Add("run")                                 // uv run
                .Add("--cache-dir", uvCacheDir)             // cache uv files in app data folder (not globally per-user)
                .Add("--python", pythonVersion!)            // python interpreter version
                //.Add("--refresh-package", "celbridge_host") // uncomment to always refresh the celbridge_host package
                .Add(packageArgs.ToArray())                 // specify the packages to install     
                .Add("python")                              // run the python interpreter
                .Add("-m", "IPython")                       // use IPython
                .Add("--no-banner")                         // don't show the IPython banner
                .Add("--ipython-dir", ipythonDir)           // use a ipython storage dir in the celbridge cache folder
                .Add("-m", "celbridge_host")                // run the celbridge module
                .Add("-i")                                  // drop to interactive mode after running celbridge module
                .ToString();

            var terminal = _workspaceWrapper.WorkspaceService.ConsoleService.Terminal;
            terminal.Start(commandLine, workingDir);

            // Create and connect RPC service
            _rpcService = _rpcServiceFactory(pipeName);
            
            // Wait for Python process to start RPC server (with timeout)
            var connectResult = await _rpcService.ConnectAsync();
            if (connectResult.IsFailure)
            {
                return Result.Fail("Failed to connect to Python RPC server")
                    .WithErrors(connectResult);
            }

            // Create Python RPC client for strongly-typed Python method calls
            _pythonRpcClient = _pythonRpcClientFactory(_rpcService);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("An error occurred when initializing Python")
                         .WithException(ex);
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
