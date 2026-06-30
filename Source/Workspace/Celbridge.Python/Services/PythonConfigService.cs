using Celbridge.ApplicationEnvironment;
using Celbridge.FileSystem;

namespace Celbridge.Python.Services;

/// <summary>
/// Provides Python configuration values read from asset files.
/// </summary>
public class PythonConfigService : IPythonConfigService
{
    private const string FallbackPythonVersion = "3.12";
    private const string PythonModuleFolder = "Celbridge.Python";

    private readonly ILocalFileSystem _fileSystem;
    private readonly IAppEnvironment _appEnvironment;

    private string? _cachedDefaultPythonVersion;

    public PythonConfigService(ILocalFileSystem fileSystem, IAppEnvironment appEnvironment)
    {
        _fileSystem = fileSystem;
        _appEnvironment = appEnvironment;
    }

    public string DefaultPythonVersion
    {
        get
        {
            if (_cachedDefaultPythonVersion is not null)
            {
                return _cachedDefaultPythonVersion;
            }

            try
            {
                var versionFilePath = _appEnvironment.GetBundledAssetPath(
                    PythonModuleFolder, "Assets/Python/python_version.txt");
                var readResult = _fileSystem.ReadAllTextAsync(versionFilePath).GetAwaiter().GetResult();
                var version = string.Empty;
                if (readResult.IsSuccess)
                {
                    version = readResult.Value.Trim();
                }

                if (!string.IsNullOrWhiteSpace(version))
                {
                    _cachedDefaultPythonVersion = version;
                    return version;
                }
            }
            catch
            {
                // If reading fails, use fallback
            }

            _cachedDefaultPythonVersion = FallbackPythonVersion;
            return FallbackPythonVersion;
        }
    }
}
