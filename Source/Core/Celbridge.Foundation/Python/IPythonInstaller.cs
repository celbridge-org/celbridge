namespace Celbridge.Python;

/// <summary>
/// Installs and refreshes the bundled Python support files (uv binary, wheels,
/// installer version marker) into the app's local data folder.
/// </summary>
public interface IPythonInstaller
{
    /// <summary>
    /// The absolute path of the Python support folder, whether or not it is installed yet.
    /// </summary>
    string PythonFolderPath { get; }

    /// <summary>
    /// Ensures the Python support files are installed for the given app version,
    /// performing a full reinstall if the on-disk version marker is missing or
    /// differs from the bundled assets. Returns the absolute path to the Python
    /// folder on success. Concurrent callers share a single install run.
    /// </summary>
    Task<Result<string>> InstallPythonAsync(string appVersion);
}
