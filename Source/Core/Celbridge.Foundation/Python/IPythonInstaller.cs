namespace Celbridge.Python;

/// <summary>
/// Installs and refreshes the bundled Python support files (uv binary, wheels,
/// installer version marker) into the app's local data folder.
/// </summary>
public interface IPythonInstaller
{
    /// <summary>
    /// Ensures the Python support files are installed for the given app version,
    /// performing a full reinstall if the on-disk version marker is missing or
    /// differs from the bundled assets. Returns the absolute path to the Python
    /// folder on success.
    /// </summary>
    Task<Result<string>> InstallPythonAsync(string appVersion);
}
