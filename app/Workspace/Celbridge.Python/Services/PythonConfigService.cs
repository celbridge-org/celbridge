namespace Celbridge.Python.Services;

/// <summary>
/// Provides Python configuration values read from asset files.
/// </summary>
public class PythonConfigService : IPythonConfigService
{
    private const string PythonVersionAssetPath = "ms-appx:///Assets/Python/python_version.txt";
    private const string FallbackPythonVersion = "3.12";

    private string? _cachedDefaultPythonVersion;

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
                var file = StorageFile.GetFileFromApplicationUriAsync(new Uri(PythonVersionAssetPath))
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();

                using var stream = file.OpenStreamForReadAsync().GetAwaiter().GetResult();
                using var reader = new StreamReader(stream);
                var version = reader.ReadToEnd().Trim();

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
