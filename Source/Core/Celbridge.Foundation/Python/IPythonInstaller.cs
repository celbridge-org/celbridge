namespace Celbridge.Python;

/// <summary>
/// Installs and refreshes the bundled Python support files (uv binary, wheel, the celbridge-py tool,
/// installer version marker) into the app's local data folder.
/// </summary>
public interface IPythonInstaller
{
    /// <summary>
    /// The absolute path of the Python support folder, whether or not it is installed yet.
    /// </summary>
    string PythonFolderPath { get; }

    /// <summary>
    /// The absolute path of the folder holding the uv and uvx executables, whether or not they are
    /// installed yet. Holds executables alone, so it is safe to put on a child process PATH.
    /// </summary>
    string UvBinFolderPath { get; }

    /// <summary>
    /// The absolute path of the uv executable, whether or not it is installed yet.
    /// </summary>
    string UvExecutablePath { get; }

    /// <summary>
    /// The absolute path of the folder holding the celbridge-py command published by the tool install,
    /// whether or not it is installed yet. Holds executables alone, so it is safe to put on a child
    /// process PATH.
    /// </summary>
    string UvToolBinFolderPath { get; }

    /// <summary>
    /// The absolute path of uv's package cache, shared by the installed tool and by every project.
    /// </summary>
    string UvCacheFolderPath { get; }

    /// <summary>
    /// The absolute path of the store of interpreters uv manages, shared by the installed tool and by
    /// every project.
    /// </summary>
    string UvPythonInstallFolderPath { get; }

    /// <summary>
    /// Ensures the Python support files are installed for the given app version, performing a full
    /// reinstall if the on-disk version marker is missing or differs from the bundled assets, and
    /// republishing the celbridge-py tool if only that is missing. Concurrent callers share a single
    /// install run.
    /// </summary>
    Task<Result> InstallPythonAsync(string appVersion);

    /// <summary>
    /// The absolute path of the celbridge wheel in the installed support folder. Fails if no install has
    /// completed.
    /// </summary>
    Task<Result<string>> GetInstalledWheelPathAsync();
}
