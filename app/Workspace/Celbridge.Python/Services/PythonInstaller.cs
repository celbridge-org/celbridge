using System.IO.Compression;

using Path = System.IO.Path;

namespace Celbridge.Python.Services;

public static class PythonInstaller
{
    private const string PythonFolderName = "Python";
    private const string PythonAssetsFolder = "Assets\\Python";
    private const string UVZipAssetPath = "ms-appx:///Assets/UV/uv-x86_64-pc-windows-msvc.zip";
    private const string BuildVersionPath = "ms-appx:///Assets/Python/build_version.txt";

    public static async Task<Result<string>> InstallPythonAsync()
    {
        try
        {
            var localFolder = ApplicationData.Current.LocalFolder;
            var pythonFolderPath = Path.Combine(localFolder.Path, PythonFolderName);

            bool needsReinstall = await CheckBuildVersionAsync(pythonFolderPath);

            if (needsReinstall)
            {
                await ReinstallAsync(localFolder, pythonFolderPath);
            }

            return Result<string>.Ok(pythonFolderPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to install Python support files")
                .WithException(ex);
        }
    }

    private static async Task<bool> CheckBuildVersionAsync(string pythonFolderPath)
    {
        // If the folder doesn't exist, we need to install
        if (!Directory.Exists(pythonFolderPath))
        {
            return true;
        }

        var localBuildVersionPath = Path.Combine(pythonFolderPath, "build_version.txt");

        // Load the GUID text in the file at BuildVersionFilePath (i.e. from embedded asset)
        var assetBuildVersionFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri(BuildVersionPath));
        string assetGuid;
        using (var assetStream = await assetBuildVersionFile.OpenStreamForReadAsync())
        using (var reader = new StreamReader(assetStream))
        {
            assetGuid = await reader.ReadToEndAsync();
            assetGuid = assetGuid.Trim();
        }

        // Load the GUID text from the build_version.txt file in pythonFolderPath (local install)
        string? localGuid = null;
        if (File.Exists(localBuildVersionPath))
        {
            using (var localStream = File.OpenRead(localBuildVersionPath))
            using (var reader = new StreamReader(localStream))
            {
                localGuid = await reader.ReadToEndAsync();
                localGuid = localGuid.Trim();
            }
        }

        // If the GUID text doesn't match, this indicates a new build, so we need to reinstall
        if (!string.Equals(assetGuid, localGuid, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static async Task ReinstallAsync(StorageFolder localFolder, string pythonFolderPath)
    {
        // Delete existing folder if it exists (handles upgrade scenario)
        if (Directory.Exists(pythonFolderPath))
        {
            Directory.Delete(pythonFolderPath, true);
        }

        var pythonFolder = await localFolder.CreateFolderAsync(PythonFolderName, CreationCollisionOption.OpenIfExists);

        // uv handles installing the required python & package versions for the loaded project
        var uvZipFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri(UVZipAssetPath));
        var uvTempFile = await uvZipFile.CopyAsync(ApplicationData.Current.TemporaryFolder, "uv.zip", NameCollisionOption.ReplaceExisting);
        ZipFile.ExtractToDirectory(uvTempFile.Path, pythonFolder.Path, overwriteFiles: true);

        // Copy the embedded Python assets to the local Python folder
        StorageFolder installedLocation = Package.Current.InstalledLocation;
        StorageFolder extrasFolder = await installedLocation.GetFolderAsync(PythonAssetsFolder);
        await CopyStorageFolderAsync(extrasFolder, pythonFolder.Path);

        // Write the asset GUID to the local build_version.txt
        var assetBuildVersionFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri(BuildVersionPath));
        string assetGuid;
        using (var assetStream = await assetBuildVersionFile.OpenStreamForReadAsync())
        using (var reader = new StreamReader(assetStream))
        {
            assetGuid = await reader.ReadToEndAsync();
            assetGuid = assetGuid.Trim();
        }

        var localBuildVersionPath = Path.Combine(pythonFolderPath, "build_version.txt");
        File.WriteAllText(localBuildVersionPath, assetGuid);
    }

    private static async Task CopyStorageFolderAsync(StorageFolder sourceFolder, string destinationPath)
    {
        if (sourceFolder == null)
        { 
            throw new ArgumentNullException(nameof(sourceFolder));
        }

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Destination path must not be empty", nameof(destinationPath));
        }

        Directory.CreateDirectory(destinationPath);

        var files = await sourceFolder.GetFilesAsync();
        foreach (var file in files)
        {
            var targetFilePath = Path.Combine(destinationPath, file.Name);
            using (var sourceStream = await file.OpenStreamForReadAsync())
            using (var destinationStream = File.Create(targetFilePath))
            {
                await sourceStream.CopyToAsync(destinationStream);
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
