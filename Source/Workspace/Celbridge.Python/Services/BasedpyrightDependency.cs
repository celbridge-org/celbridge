using Celbridge.Dependencies;
using Celbridge.FileSystem;
using Celbridge.Localization;
using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.Utilities;

namespace Celbridge.Python.Services;

/// <summary>
/// The pinned release of basedpyright, the language server for Python. uv installs each version as a tool. Each
/// version has a folder in the dependencies folder, named by the version. The check looks for the server's
/// executable in the folder, and for the interpreter behind the tool environment.
/// </summary>
public sealed class BasedpyrightDependency : IDependency
{
    /// <summary>
    /// The version of basedpyright to install.
    /// </summary>
    public const string PinnedVersion = "1.40.2";

    private const string PackageName = "basedpyright";
    private const string LanguageServersFolderName = "LanguageServers";
    private const string BasedpyrightFolderName = "basedpyright";
    private const string BinFolderName = "bin";
    private const string PyVenvConfigFileName = "pyvenv.cfg";
    private const string ServerExecutableName = "basedpyright-langserver";
    private const string ServerExecutableNameWindows = "basedpyright-langserver.exe";

    // The timeout is generous. The install downloads basedpyright and a Node runtime.
    private static readonly TimeSpan ToolInstallTimeout = TimeSpan.FromMinutes(5);

    private readonly ILocalFileSystem _fileSystem;
    private readonly IAppEnvironment _appEnvironment;
    private readonly IPythonConfigService _pythonConfigService;
    private readonly ILocalizerService _localizerService;
    private readonly IUvRunner _uvRunner;
    private readonly CelbridgeWheelFolders _folders;
    private readonly ILogger<BasedpyrightDependency> _logger;

    public BasedpyrightDependency(
        ILocalFileSystem fileSystem,
        IAppEnvironment appEnvironment,
        IPythonConfigService pythonConfigService,
        ILocalizerService localizerService,
        IUvRunner uvRunner,
        CelbridgeWheelFolders folders,
        ILogger<BasedpyrightDependency> logger)
        : this(fileSystem, appEnvironment, pythonConfigService, localizerService, uvRunner, folders, logger, PinnedVersion)
    {
    }

    internal BasedpyrightDependency(
        ILocalFileSystem fileSystem,
        IAppEnvironment appEnvironment,
        IPythonConfigService pythonConfigService,
        ILocalizerService localizerService,
        IUvRunner uvRunner,
        CelbridgeWheelFolders folders,
        ILogger<BasedpyrightDependency> logger,
        string version)
    {
        _fileSystem = fileSystem;
        _appEnvironment = appEnvironment;
        _pythonConfigService = pythonConfigService;
        _localizerService = localizerService;
        _uvRunner = uvRunner;
        _folders = folders;
        _logger = logger;
        Version = version;
    }

    public string Id => "basedpyright";

    public string Version { get; }

    public string Name => _localizerService.GetString("Dependency_Basedpyright_Name");

    public string ProgressTitle => _localizerService.GetString("Dependency_Basedpyright_ProgressTitle");

    /// <summary>
    /// The folder that contains the pinned version's tool environment, whether or not it is installed yet. Each
    /// version has a folder named by the version.
    /// </summary>
    public string FolderPath => Path.Combine(VersionsFolderPath, Version);

    /// <summary>
    /// The pinned version's language server executable, whether or not it is installed yet.
    /// </summary>
    public string ExecutablePath => Path.Combine(
        BinFolderPath,
        OperatingSystem.IsWindows() ? ServerExecutableNameWindows : ServerExecutableName);

    private string BinFolderPath => Path.Combine(FolderPath, BinFolderName);

    // uv names the tool's environment after the package.
    private string PyVenvConfigPath => Path.Combine(FolderPath, PackageName, PyVenvConfigFileName);

    private string VersionsFolderPath => Path.Combine(
        _appEnvironment.DependenciesFolderPath,
        LanguageServersFolderName,
        BasedpyrightFolderName);

    public async Task<bool> IsInstalledAsync()
    {
        if (await _fileSystem.GetKindAsync(ExecutablePath) != StorageItemKind.File)
        {
            return false;
        }

        // The executable is a launcher, and the launcher outlives the interpreter it starts. The interpreter is in
        // the shared store of interpreters, outside the version's folder.
        var toolInterpreter = new ToolEnvironmentInterpreter(_fileSystem);

        return await toolInterpreter.ExistsAsync(PyVenvConfigPath);
    }

    public async Task<Result> InstallAsync()
    {
        // Another copy of Celbridge may have installed the version.
        if (await IsInstalledAsync())
        {
            return Result.Ok();
        }

        var pythonVersion = _pythonConfigService.DefaultPythonVersion;

        _logger.LogInformation("Installing basedpyright {Version} as a uv tool with Python {PythonVersion}", Version, pythonVersion);

        // --force replaces what an install that stopped part way left in the folder. --no-config stops uv reading
        // any configuration file. A user's uv.toml then cannot block the install or change what it installs.
        var arguments = new List<string>
        {
            "tool",
            "install",
            "--force",
            "--no-config",
            "--python", pythonVersion,
            "--managed-python",
            $"{PackageName}=={Version}",
        };

        // The interpreters and the package cache are the ones the celbridge wheel's install uses.
        var variables = new Dictionary<string, string>
        {
            ["UV_TOOL_DIR"] = FolderPath,
            ["UV_TOOL_BIN_DIR"] = BinFolderPath,
            ["UV_PYTHON_INSTALL_DIR"] = _folders.UvPythonInstallFolderPath,
            ["UV_CACHE_DIR"] = _folders.UvCacheFolderPath,
        };

        var installer = new UvToolInstaller(_uvRunner, _logger);
        var installResult = await installer.InstallAsync(arguments, variables, ToolInstallTimeout);
        var isInstalled = await IsInstalledAsync();
        if (installResult.IsFailure)
        {
            // Another copy of Celbridge may have installed the version during this install. uv then fails to
            // replace the files of that copy's running server.
            if (isInstalled)
            {
                _logger.LogInformation("basedpyright {Version} was installed by another copy of Celbridge", Version);
                return Result.Ok();
            }

            return Result.Fail($"Failed to install basedpyright {Version}")
                .WithErrors(installResult);
        }

        if (!isInstalled)
        {
            return Result.Fail($"The basedpyright install left no language server at '{ExecutablePath}'");
        }

        var cleaner = new VersionedFolderCleaner(_fileSystem, _logger);
        await cleaner.DeleteOtherVersionsAsync(FolderPath);

        return Result.Ok();
    }
}
