using Celbridge.Platform;

namespace Celbridge.Python.Services;

/// <summary>
/// Gives the paths of the celbridge wheel's folders and files, whether or not each item exists yet. The Python
/// folder, uv's cache and the interpreter store are in the dependencies folder. The install lock is in the
/// application's data folder.
/// </summary>
public sealed class CelbridgeWheelFolders
{
    private const string PythonFolderName = "python";
    private const string UvToolsFolderName = "uv_tools";
    private const string UvToolBinFolderName = "uv_bin";
    private const string UvCacheFolderName = "uv_cache";
    private const string UvPythonInstallsFolderName = "uv_python_installs";
    private const string InstalledVersionFileName = "installed_version.txt";
    private const string InstallLockFileName = "install.lock";
    private const string CelbridgeToolName = "celbridge";
    private const string CelbridgeToolCommand = "celbridge-py";
    private const string PyVenvConfigFileName = "pyvenv.cfg";

    // uv builds the temporary environments in this bucket of uv's cache. Each REPL runs in one of the environments.
    private const string TemporaryEnvironmentsFolderName = "builds-v0";

    private readonly IAppEnvironment _appEnvironment;

    public CelbridgeWheelFolders(IAppEnvironment appEnvironment)
    {
        _appEnvironment = appEnvironment;
    }

    /// <summary>
    /// The Python folder. It contains the celbridge wheel, the installed tool and the version marker. A reinstall
    /// deletes the whole folder.
    /// </summary>
    public string PythonFolderPath => Path.Combine(_appEnvironment.DependenciesFolderPath, PythonFolderName);

    /// <summary>
    /// The version marker file, which records what the Python folder was built from.
    /// </summary>
    public string InstalledVersionFilePath => Path.Combine(PythonFolderPath, InstalledVersionFileName);

    /// <summary>
    /// The install lock. Every instance of the application takes the lock to install, because all of them share the
    /// Python folder.
    /// </summary>
    public string InstallLockFilePath => Path.Combine(_appEnvironment.LocalApplicationDataFolderPath, InstallLockFileName);

    /// <summary>
    /// The folder of the celbridge tool's environment. The tool install creates the environment.
    /// </summary>
    public string UvToolsFolderPath => Path.Combine(PythonFolderPath, UvToolsFolderName);

    /// <summary>
    /// The folder of the celbridge-py command. The folder contains executables alone, so it is safe to put on a child
    /// process PATH.
    /// </summary>
    public string UvToolBinFolderPath => Path.Combine(PythonFolderPath, UvToolBinFolderName);

    /// <summary>
    /// The celbridge-py command. The tool install publishes it.
    /// </summary>
    public string CelbridgeToolCommandPath => Path.Combine(
        UvToolBinFolderPath,
        OperatingSystem.IsWindows() ? $"{CelbridgeToolCommand}.exe" : CelbridgeToolCommand);

    /// <summary>
    /// The celbridge tool environment's own interpreter.
    /// </summary>
    public string CelbridgeToolInterpreterPath => OperatingSystem.IsWindows()
        ? Path.Combine(UvToolsFolderPath, CelbridgeToolName, "Scripts", "python.exe")
        : Path.Combine(UvToolsFolderPath, CelbridgeToolName, "bin", "python");

    /// <summary>
    /// The celbridge tool environment's pyvenv.cfg. Its home key names the folder of the environment's interpreter.
    /// </summary>
    public string CelbridgeToolConfigPath => Path.Combine(UvToolsFolderPath, CelbridgeToolName, PyVenvConfigFileName);

    /// <summary>
    /// uv's package cache, shared by the installed tool and by every project. The cache is beside the Python folder,
    /// so a reinstall leaves it in place, and a rebuilt wheel downloads nothing.
    /// </summary>
    public string UvCacheFolderPath => Path.Combine(_appEnvironment.DependenciesFolderPath, UvCacheFolderName);

    /// <summary>
    /// The store of interpreters uv manages, shared by the installed tool and by every project. The installed tool
    /// runs on an interpreter from here. The store is beside the Python folder, so a reinstall leaves it in place.
    /// </summary>
    public string UvPythonInstallFolderPath =>
        Path.Combine(_appEnvironment.DependenciesFolderPath, UvPythonInstallsFolderName);

    /// <summary>
    /// The folder in uv's cache that contains uv run's temporary environments, one for each REPL.
    /// </summary>
    public string TemporaryEnvironmentsFolderPath => Path.Combine(UvCacheFolderPath, TemporaryEnvironmentsFolderName);
}
