using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
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

#if !WINDOWS
    // On the Skia desktop and macOS heads the bundled assets are not packaged via ms-appx; the Uno
    // library layout copies them next to the assembly under "<base>/Celbridge.Python/Assets". Local
    // app-data (the install target) follows the same convention as ApplicationStore: a "Celbridge"
    // subfolder under the platform local-application-data folder.
    private const string BundledAssetsModuleFolder = "Celbridge.Python";
    private const string BundledAssetsFolderName = "Assets";
    private const string UVAssetSubfolder = "UV";
    private const string PythonAssetSubfolder = "Python";
    private const string ApplicationDataFolderName = "Celbridge";
#endif

    private readonly ILocalFileSystem _fileSystem;
    private readonly ILogger<PythonInstaller> _logger;

    public PythonInstaller(
        ILocalFileSystem fileSystem,
        ILogger<PythonInstaller> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<Result<string>> InstallPythonAsync(string appVersion)
    {
        try
        {
            var pythonFolderPath = Path.Combine(GetLocalApplicationDataPath(), PythonFolderName);

            bool needsReinstall = await IsInstallRequiredAsync(pythonFolderPath, appVersion);

            if (needsReinstall)
            {
                _logger.LogInformation("Running full Python reinstall at {Path}", pythonFolderPath);
                await ReinstallAsync(pythonFolderPath, appVersion);
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

    // Resolves the local application-data folder that holds the installed uv binary and Python assets.
    // The packaged Windows head uses its app-data LocalFolder; the Skia heads mirror ApplicationStore by
    // placing a Celbridge subfolder under the platform local-application-data folder.
    private static string GetLocalApplicationDataPath()
    {
#if WINDOWS
        return ApplicationData.Current.LocalFolder.Path;
#else
        var localDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localDataPath, ApplicationDataFolderName);
#endif
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
#if WINDOWS
        var assetsFolder = Path.Combine(Package.Current.InstalledLocation.Path, PythonAssetsFolder);
#else
        var assetsFolder = Path.Combine(GetBundledAssetsFolderPath(), PythonAssetSubfolder);
#endif
        var enumerateFilesResult = await _fileSystem.EnumerateAsync(assetsFolder, WheelFilePattern, recursive: false);
        if (enumerateFilesResult.IsSuccess)
        {
            var wheelFile = enumerateFilesResult.Value.FirstOrDefault(entry => !entry.IsFolder);
            if (wheelFile is not null)
            {
                wheelHash = await FileHashHelper.HashFileContentsAsync(wheelFile.FullPath);
            }
        }

        return $"{appVersion}\n{wheelHash}";
    }

    private async Task ReinstallAsync(string pythonFolderPath, string currentVersion)
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

        await _fileSystem.CreateFolderAsync(pythonFolderPath);

#if WINDOWS
        // Packaged WinUI head: bundled assets are addressed via ms-appx and the package install location.
        // uv handles installing the required python & package versions for the loaded project
        var uvZipFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri(UVZipAssetPath));
        var uvTempFile = await uvZipFile.CopyAsync(ApplicationData.Current.TemporaryFolder, UVTempFileName, NameCollisionOption.ReplaceExisting);
        ZipFile.ExtractToDirectory(uvTempFile.Path, pythonFolderPath, overwriteFiles: true);

        // Copy the embedded Python assets to the local Python folder
        StorageFolder installedLocation = Package.Current.InstalledLocation;
        StorageFolder pythonAssetsFolder = await installedLocation.GetFolderAsync(PythonAssetsFolder);
        await CopyStorageFolderAsync(pythonAssetsFolder, pythonFolderPath);
#else
        // Skia desktop / macOS / Linux head: ms-appx and Package.Current are packaged-only, so the
        // bundled assets are read from the library-layout folder next to the assembly via
        // AppContext.BaseDirectory. uv handles installing the required python & package versions.
        var bundledAssetsFolder = GetBundledAssetsFolderPath();

        var uvArchivePath = Path.Combine(bundledAssetsFolder, UVAssetSubfolder, GetUvArchiveFileName());
        await ExtractUvArchiveAsync(uvArchivePath, pythonFolderPath);

        // Copy the bundled Python assets to the local Python folder
        var pythonAssetsPath = Path.Combine(bundledAssetsFolder, PythonAssetSubfolder);
        await CopyBundledFolderAsync(pythonAssetsPath, pythonFolderPath);
#endif

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

#if !WINDOWS
    private static string GetBundledAssetsFolderPath()
    {
        return Path.Combine(AppContext.BaseDirectory, BundledAssetsModuleFolder, BundledAssetsFolderName);
    }

    // Returns the uv release archive filename for the running OS and architecture, matching the DownloadUv
    // MSBuild target in Celbridge.Python.csproj. Windows ships a .zip with the binaries at the root; macOS
    // and Linux ship a .tar.gz whose binaries live under a single top-level folder.
    private static string GetUvArchiveFileName()
    {
        bool isArm64 = RuntimeInformation.OSArchitecture == Architecture.Arm64;

        if (OperatingSystem.IsWindows())
        {
            return "uv-x86_64-pc-windows-msvc.zip";
        }

        if (OperatingSystem.IsMacOS())
        {
            return isArm64
                ? "uv-aarch64-apple-darwin.tar.gz"
                : "uv-x86_64-apple-darwin.tar.gz";
        }

        return isArm64
            ? "uv-aarch64-unknown-linux-gnu.tar.gz"
            : "uv-x86_64-unknown-linux-gnu.tar.gz";
    }

    // Extracts the bundled uv archive into the Python folder. On Windows (the Skia desktop head can run
    // there too) the archive is a .zip with the binaries at the root. On macOS and Linux it is a .tar.gz
    // whose binaries sit under a single top-level folder (e.g. uv-aarch64-apple-darwin/uv); that folder is
    // stripped so the binary lands directly in the Python folder, matching the layout the rest of the
    // service expects. TarFile preserves the Unix executable mode and the flattening move is a rename that
    // preserves it, so no explicit chmod is needed.
    private async Task ExtractUvArchiveAsync(string uvArchivePath, string pythonFolderPath)
    {
        if (OperatingSystem.IsWindows())
        {
            ZipFile.ExtractToDirectory(uvArchivePath, pythonFolderPath, overwriteFiles: true);
            return;
        }

        var archiveBytesResult = await _fileSystem.ReadAllBytesAsync(uvArchivePath);
        if (archiveBytesResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to read the uv archive '{uvArchivePath}': {archiveBytesResult.FirstErrorMessage}");
        }
        var archiveBytes = archiveBytesResult.Value;

        using (var archiveStream = new MemoryStream(archiveBytes))
        using (var gzipStream = new GZipStream(archiveStream, CompressionMode.Decompress))
        {
            TarFile.ExtractToDirectory(gzipStream, pythonFolderPath, overwriteFiles: true);
        }

        // The tarball extracts a single top-level folder named after the archive (without the .tar.gz
        // suffix). Move its files up so the uv binary sits directly in the Python folder.
        var topLevelFolderName = Path.GetFileName(uvArchivePath).Replace(".tar.gz", string.Empty);
        var extractedFolder = Path.Combine(pythonFolderPath, topLevelFolderName);

        var enumerateResult = await _fileSystem.EnumerateAsync(extractedFolder, "*", recursive: false);
        if (enumerateResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to enumerate the extracted uv folder '{extractedFolder}': {enumerateResult.FirstErrorMessage}");
        }

        foreach (var entry in enumerateResult.Value)
        {
            if (entry.IsFolder)
            {
                continue;
            }

            var destPath = Path.Combine(pythonFolderPath, Path.GetFileName(entry.FullPath));
            var moveResult = await _fileSystem.MoveFileAsync(entry.FullPath, destPath);
            if (moveResult.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Failed to move uv binary '{entry.FullPath}' to '{destPath}': {moveResult.FirstErrorMessage}");
            }
        }

        await _fileSystem.DeleteFolderAsync(extractedFolder, recursive: true);
    }

    // Recursively copies a bundled-asset folder through the filesystem gateway. Mirrors
    // CopyStorageFolderAsync but works from plain paths rather than WinRT StorageFolders,
    // since the Skia/macOS heads address bundled assets by AppContext.BaseDirectory.
    private async Task CopyBundledFolderAsync(string sourcePath, string destinationPath)
    {
        await _fileSystem.CreateFolderAsync(destinationPath);

        var enumerateResult = await _fileSystem.EnumerateAsync(sourcePath, "*", recursive: true);
        if (enumerateResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to enumerate bundled assets folder '{sourcePath}': {enumerateResult.FirstErrorMessage}");
        }

        foreach (var entry in enumerateResult.Value)
        {
            if (entry.IsFolder)
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(sourcePath, entry.FullPath);
            var targetPath = Path.Combine(destinationPath, relativePath);

            var targetFolder = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetFolder))
            {
                await _fileSystem.CreateFolderAsync(targetFolder);
            }

            var copyResult = await _fileSystem.CopyFileAsync(entry.FullPath, targetPath);
            if (copyResult.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Failed to copy bundled asset '{entry.FullPath}' to '{targetPath}': {copyResult.FirstErrorMessage}");
            }
        }
    }
#endif
}
