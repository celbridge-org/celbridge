using System.IO.Compression;
using System.Runtime.Versioning;
using Celbridge.FileSystem;
using Celbridge.Logging;
using Celbridge.Utilities;

namespace Celbridge.Python.Services;

public class PythonInstaller : IPythonInstaller
{
    private const string PythonFolderName = "Python";
    private const string PythonAssetsFolder = "Assets\\Python";
    private const string UVZipAssetPath = "ms-appx:///Assets/UV/uv-x86_64-pc-windows-msvc.zip";
    private const string InstalledVersionFileName = "installed_version.txt";
    private const string WheelFilePattern = "celbridge-*.whl";
    private const string UVTempFileName = "uv.zip";

    private readonly ILocalFileSystem _fileSystem;
    private readonly ILogger<PythonInstaller> _logger;

    public PythonInstaller(
        ILocalFileSystem fileSystem,
        ILogger<PythonInstaller> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    [SupportedOSPlatform("windows10.0.10240.0")]
    public async Task<Result<string>> InstallPythonAsync(string appVersion)
    {
        try
        {
            var localFolder = ApplicationData.Current.LocalFolder;
            var pythonFolderPath = Path.Combine(localFolder.Path, PythonFolderName);

            bool needsReinstall = await IsInstallRequiredAsync(pythonFolderPath, appVersion);

            if (needsReinstall)
            {
                _logger.LogInformation("Running full Python reinstall at {Path}", pythonFolderPath);
                await ReinstallAsync(localFolder, pythonFolderPath, appVersion);
                _logger.LogInformation("Python reinstall completed");
            }

            return Result<string>.Ok(pythonFolderPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to install Python support files")
                .WithException(ex);
        }
    }

    private async Task<bool> IsInstallRequiredAsync(string pythonFolderPath, string currentVersion)
    {
        // If the python folder doesn't exist, we need to install
        var pythonFolderInfoResult = await _fileSystem.GetInfoAsync(pythonFolderPath);
        bool pythonFolderExists = pythonFolderInfoResult.IsSuccess
            && pythonFolderInfoResult.Value.Kind == StorageItemKind.Folder;
        if (!pythonFolderExists)
        {
            _logger.LogDebug("Python reinstall required: pythonFolder does not exist at {Path}", pythonFolderPath);
            return true;
        }

        var installedVersionPath = Path.Combine(pythonFolderPath, InstalledVersionFileName);

        // If version file doesn't exist, we need to install
        var installedVersionInfoResult = await _fileSystem.GetInfoAsync(installedVersionPath);
        bool installedVersionExists = installedVersionInfoResult.IsSuccess
            && installedVersionInfoResult.Value.Kind == StorageItemKind.File;
        if (!installedVersionExists)
        {
            _logger.LogDebug("Python reinstall required: installed_version.txt missing at {Path}", installedVersionPath);
            return true;
        }

        // Read the installed version and compare.
        // The installed version file contains both the app version and the build version
        // (separated by a newline) so that changes to either trigger a reinstall.
        var readResult = await _fileSystem.ReadAllTextAsync(installedVersionPath);
        if (readResult.IsFailure)
        {
            _logger.LogDebug("Python reinstall required: installed_version.txt unreadable at {Path}", installedVersionPath);
            return true;
        }
        var installedVersionContent = readResult.Value.Trim();
        var expectedVersionContent = await GetVersionContentAsync(currentVersion);

        if (!string.Equals(expectedVersionContent, installedVersionContent, StringComparison.Ordinal))
        {
            _logger.LogDebug(
                "Python reinstall required: installed_version.txt mismatch. Installed='{Installed}' Expected='{Expected}'",
                installedVersionContent.Replace("\n", "\\n"),
                expectedVersionContent.Replace("\n", "\\n"));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Builds the version content string that combines the app version and a hash
    /// of the wheel file contents. This is written to the installed version file
    /// and compared on subsequent runs to detect when either the app or the Python
    /// package has changed.
    /// </summary>
    private async Task<string> GetVersionContentAsync(string appVersion)
    {
        // Non-critical: if we can't hash the wheel, the app version alone
        // still triggers reinstalls on app updates.
        var wheelHash = "";
        var installedLocation = Package.Current.InstalledLocation;
        var assetsFolder = Path.Combine(installedLocation.Path, PythonAssetsFolder);
        var enumerateFilesResult = await _fileSystem.EnumerateAsync(assetsFolder, WheelFilePattern, recursive: false);
        if (enumerateFilesResult.IsSuccess)
        {
            var wheelFile = enumerateFilesResult.Value.FirstOrDefault(entry => !entry.IsFolder);
            if (wheelFile is not null)
            {
                wheelHash = FileHashHelper.HashFileContents(wheelFile.FullPath);
            }
        }

        return $"{appVersion}\n{wheelHash}";
    }

    [SupportedOSPlatform("windows10.0.10240.0")]
    private async Task ReinstallAsync(StorageFolder localFolder, string pythonFolderPath, string currentVersion)
    {
        // Delete existing folder if it exists (handles upgrade scenario).
        var pythonFolderInfoResult = await _fileSystem.GetInfoAsync(pythonFolderPath);
        bool pythonFolderExists = pythonFolderInfoResult.IsSuccess
            && pythonFolderInfoResult.Value.Kind == StorageItemKind.Folder;
        if (pythonFolderExists)
        {
            var deleteResult = await _fileSystem.DeleteFolderAsync(pythonFolderPath, recursive: true);
            if (deleteResult.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Failed to delete existing Python folder '{pythonFolderPath}'. " +
                    "A previous Python process may still be running with locked files. " +
                    "Close all Celbridge instances and try again.",
                    deleteResult.FirstException);
            }
        }

        var pythonFolder = await localFolder.CreateFolderAsync(PythonFolderName, CreationCollisionOption.OpenIfExists);

        // uv handles installing the required python & package versions for the loaded project
        var uvZipFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri(UVZipAssetPath));
        var uvTempFile = await uvZipFile.CopyAsync(ApplicationData.Current.TemporaryFolder, UVTempFileName, NameCollisionOption.ReplaceExisting);
        ZipFile.ExtractToDirectory(uvTempFile.Path, pythonFolder.Path, overwriteFiles: true);

        // Copy the embedded Python assets to the local Python folder
        StorageFolder installedLocation = Package.Current.InstalledLocation;
        StorageFolder pythonAssetsFolder = await installedLocation.GetFolderAsync(PythonAssetsFolder);
        await CopyStorageFolderAsync(pythonAssetsFolder, pythonFolder.Path);

        // Write the version file after successful install.
        // This signals that the install completed successfully and includes both the app
        // version and the build version so that changes to either trigger a reinstall.
        var versionFile = Path.Combine(pythonFolderPath, InstalledVersionFileName);
        var versionContent = await GetVersionContentAsync(currentVersion);
        await _fileSystem.WriteAllTextAsync(versionFile, versionContent);
    }

    private async Task CopyStorageFolderAsync(StorageFolder sourceFolder, string destinationPath)
    {
        if (sourceFolder == null)
        {
            throw new ArgumentNullException(nameof(sourceFolder));
        }

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Destination path must not be empty", nameof(destinationPath));
        }

        await _fileSystem.CreateFolderAsync(destinationPath);

        var files = await sourceFolder.GetFilesAsync();
        foreach (var file in files)
        {
            var targetFilePath = Path.Combine(destinationPath, file.Name);
            // Buffer the source stream into memory then write through the
            // filesystem abstraction. Python assets are small individual files
            // (scripts and wheels), so loading them fully into memory is fine.
            using (var sourceStream = await file.OpenStreamForReadAsync())
            using (var bufferStream = new MemoryStream())
            {
                await sourceStream.CopyToAsync(bufferStream);
                await _fileSystem.WriteAllBytesAsync(targetFilePath, bufferStream.ToArray());
            }
        }

        var subfolders = await sourceFolder.GetFoldersAsync();
        foreach (var subfolder in subfolders)
        {
            var subfolderPath = Path.Combine(destinationPath, subfolder.Name);
            await CopyStorageFolderAsync(subfolder, subfolderPath);
        }
    }
}
