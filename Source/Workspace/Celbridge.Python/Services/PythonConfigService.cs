#if !WINDOWS
using Celbridge.FileSystem;
#endif

namespace Celbridge.Python.Services;

/// <summary>
/// Provides Python configuration values read from asset files.
/// </summary>
public class PythonConfigService : IPythonConfigService
{
    private const string FallbackPythonVersion = "3.12";

    private string? _cachedDefaultPythonVersion;

#if WINDOWS
    private const string PythonVersionAssetPath = "ms-appx:///Assets/Python/python_version.txt";
#else
    private readonly ILocalFileSystem _fileSystem;

    public PythonConfigService(ILocalFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }
#endif

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
#if WINDOWS
                var file = StorageFile.GetFileFromApplicationUriAsync(new Uri(PythonVersionAssetPath))
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();

                using var stream = file.OpenStreamForReadAsync().GetAwaiter().GetResult();
                using var reader = new StreamReader(stream);
                var version = reader.ReadToEnd().Trim();
#else
                // The Skia desktop and macOS heads have no ms-appx; the bundled version file
                // ships next to the assembly under the Uno library layout.
                var versionFilePath = Path.Combine(
                    AppContext.BaseDirectory, "Celbridge.Python", "Assets", "Python", "python_version.txt");
                var readResult = _fileSystem.ReadAllTextAsync(versionFilePath).GetAwaiter().GetResult();
                var version = string.Empty;
                if (readResult.IsSuccess)
                {
                    version = readResult.Value.Trim();
                }
#endif

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
