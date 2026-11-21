using Celbridge.FileSystem;

namespace Celbridge.Python.Services;

/// <summary>
/// Checks the interpreter behind a uv tool environment. The environment runs on a shared interpreter in the store
/// of interpreters, and the environment's pyvenv.cfg names the interpreter's folder in its home key.
/// </summary>
public sealed class ToolEnvironmentInterpreter
{
    private const string PyVenvHomeKey = "home";
    private const string PythonExecutableNameWindows = "python.exe";

    private readonly ILocalFileSystem _fileSystem;

    public ToolEnvironmentInterpreter(ILocalFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Returns true if the shared interpreter named by the pyvenv.cfg exists. Returns false when the pyvenv.cfg is
    /// missing or has no home key.
    /// </summary>
    public async Task<bool> ExistsAsync(string pyVenvConfigPath)
    {
        var readResult = await _fileSystem.ReadAllTextAsync(pyVenvConfigPath);
        if (readResult.IsFailure)
        {
            return false;
        }
        var configText = readResult.Value;

        var interpreterHomePath = ReadPyVenvHome(configText);
        if (string.IsNullOrEmpty(interpreterHomePath))
        {
            return false;
        }

        // On Windows the environment's own interpreter is a launcher, and the launcher outlives the interpreter it
        // starts. A junction to a deleted folder also still reads as a folder. So the check looks for the
        // interpreter itself.
        if (OperatingSystem.IsWindows())
        {
            var interpreterPath = Path.Combine(interpreterHomePath, PythonExecutableNameWindows);
            return await _fileSystem.GetKindAsync(interpreterPath) == StorageItemKind.File;
        }

        return await _fileSystem.GetKindAsync(interpreterHomePath) == StorageItemKind.Folder;
    }

    // Returns the value of the home key in the text of a pyvenv.cfg, or null when the text has no home key.
    internal static string? ReadPyVenvHome(string configText)
    {
        foreach (var line in configText.Split('\n'))
        {
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            if (!key.Equals(PyVenvHomeKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return line[(separatorIndex + 1)..].Trim();
        }

        return null;
    }
}
