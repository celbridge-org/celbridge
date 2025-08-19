using System.IO.Compression;

using Path = System.IO.Path;

namespace Celbridge.Python.Services;

public static class PythonInstaller
{
    private const string PythonFolderName = "Python";
    private const string PythonZipAssetPath = "ms-appx:///Assets/EmbeddedPython/python-3.13.6-embed-amd64.zip";

    public static async Task<Result<string>> InstallPythonAsync()
    {
        try
        {
            var localFolder = ApplicationData.Current.LocalFolder;
            var pythonFolderPath = Path.Combine(localFolder.Path, PythonFolderName);

            // Uncomment to force install embedded Python
            //if (Directory.Exists(pythonFolderPath))
            //{
            //    Directory.Delete(pythonFolderPath, true);
            //}

            if (!Directory.Exists(pythonFolderPath))
            {
                var pythonFolder = await localFolder.CreateFolderAsync(PythonFolderName, CreationCollisionOption.OpenIfExists);

                // Unzip embedded Python
                var pythonZipFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri(PythonZipAssetPath));
                var pythonTempFile = await pythonZipFile.CopyAsync(ApplicationData.Current.TemporaryFolder, "python.zip", NameCollisionOption.ReplaceExisting);
                ZipFile.ExtractToDirectory(pythonTempFile.Path, pythonFolder.Path, overwriteFiles: true);

                StorageFolder installedLocation = Package.Current.InstalledLocation;
                StorageFolder extrasFolder = await installedLocation.GetFolderAsync("Assets\\PythonExtras");
                await CopyStorageFolderAsync(extrasFolder, pythonFolder.Path);

                // Unzip lib.zip and delete the zip file
                var libZipPath = Path.Combine(pythonFolderPath, "lib.zip");
                ZipFile.ExtractToDirectory(libZipPath, pythonFolder.Path, overwriteFiles: true);
                File.Delete(libZipPath);
            }

            return Result<string>.Ok(pythonFolderPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to install embedded Python to: pythonFolderPath")
                .WithException(ex);
        }
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
