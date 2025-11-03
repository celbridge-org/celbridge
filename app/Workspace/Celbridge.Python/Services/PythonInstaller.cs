using System.IO.Compression;
using System.Diagnostics;

using Path = System.IO.Path;

namespace Celbridge.Python.Services;

public static class PythonInstaller
{
    private const string PythonFolderName = "Python";
    private const string PythonAssetsFolder = "Assets\\Python";
    private const string UVZipAssetPath = "ms-appx:///Assets/UV/uv-x86_64-pc-windows-msvc.zip";
    private const string BuildVersionPath = "ms-appx:///Assets/Python/build_version.txt";
    private const string BuildVersionFileName = "build_version.txt";
    private const string UVToolsSubfolder = "uv_tools";
    private const string UVBinSubfolder = "uv_bin";
    private const string UVExecutableName = "uv.exe";
    private const string UVTempFileName = "uv.zip";

    public static async Task<Result<string>> InstallPythonAsync()
    {
        try
        {
            var localFolder = ApplicationData.Current.LocalFolder;
            var pythonFolderPath = Path.Combine(localFolder.Path, PythonFolderName);

            bool needsReinstall = await IsInstallRequiredAsync(pythonFolderPath);

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

    private static async Task<bool> IsInstallRequiredAsync(string pythonFolderPath)
    {
        // If the python folder doesn't exist, we need to install
        if (!Directory.Exists(pythonFolderPath))
        {
            return true;
        }

        var localBuildVersionPath = Path.Combine(pythonFolderPath, BuildVersionFileName);

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
        var uvTempFile = await uvZipFile.CopyAsync(ApplicationData.Current.TemporaryFolder, UVTempFileName, NameCollisionOption.ReplaceExisting);
        ZipFile.ExtractToDirectory(uvTempFile.Path, pythonFolder.Path, overwriteFiles: true);

        // Copy the embedded Python assets to the local Python folder
        StorageFolder installedLocation = Package.Current.InstalledLocation;
        StorageFolder pythonAssetsFolder = await installedLocation.GetFolderAsync(PythonAssetsFolder);
        await CopyStorageFolderAsync(pythonAssetsFolder, pythonFolder.Path);

        // Rename build_version.txt to a temporary file (in case the celbridge tool install step fails)
        var versionFile = Path.Combine(pythonFolderPath, BuildVersionFileName);
        var tempVersionFile = Path.Combine(pythonFolderPath, BuildVersionFileName + ".temp");
        File.Move(versionFile, tempVersionFile);

        // Install the celbridge package as a tool using uv
        await InstallCelbridgeToolAsync(pythonFolderPath);

        // The install process examines build_version.txt to determine if a reinstall is needed
        // This step signals that the install completed successfully.
        File.Move(tempVersionFile, versionFile);
    }

    private static async Task InstallCelbridgeToolAsync(string pythonFolderPath)
    {
        // Create directories for uv tools and binaries
        var uvToolDir = Path.Combine(pythonFolderPath, UVToolsSubfolder);
        var uvToolBinDir = Path.Combine(pythonFolderPath, UVBinSubfolder);
        Directory.CreateDirectory(uvToolDir);
        Directory.CreateDirectory(uvToolBinDir);

        var uvExePath = Path.Combine(pythonFolderPath, UVExecutableName);
        if (!File.Exists(uvExePath))
        {
            throw new FileNotFoundException($"uv executable not found at {uvExePath}");
        }

        // Find the celbridge wheel file dynamically
        var findWheelResult = FindWheelFile(pythonFolderPath, "celbridge");
        if (findWheelResult.IsFailure)
        {
            throw new FileNotFoundException(findWheelResult.Error);
        }
        var celbridgeWheelPath = findWheelResult.Value;

        // Configure the process to run uv tool install
        // Use --force to overwrite any existing installation
        var processStartInfo = new ProcessStartInfo
        {
            FileName = uvExePath,
            Arguments = $"tool install --force \"{celbridgeWheelPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = pythonFolderPath
        };

        // Set environment variables for UV_TOOL_DIR and UV_TOOL_BIN_DIR
        // These must be set in the process environment to override uv's default locations
        processStartInfo.EnvironmentVariables["UV_TOOL_DIR"] = uvToolDir;
        processStartInfo.EnvironmentVariables["UV_TOOL_BIN_DIR"] = uvToolBinDir;

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();

        // Read output and error streams
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            var errorMessage = $"Failed to install celbridge tool using uv." +
                $"\nCommand: {uvExePath} {processStartInfo.Arguments}" +
                $"\nWorking Directory: {pythonFolderPath}" +
                $"\nExit Code: {process.ExitCode}" +
                $"\nUV_TOOL_DIR: {uvToolDir}" +
                $"\nUV_TOOL_BIN_DIR: {uvToolBinDir}";

            if (!string.IsNullOrWhiteSpace(error))
            {
                errorMessage += $"\nStderr: {error}";
            }

            if (!string.IsNullOrWhiteSpace(output))
            {
                errorMessage += $"\nStdout: {output}";
            }

            throw new InvalidOperationException(errorMessage);
        }
    }

    private static Result<string> FindWheelFile(string folderPath, string packageName)
    {
        try
        {
            var searchPattern = $"{packageName}-*.whl";
            var wheelFiles = Directory.GetFiles(folderPath, searchPattern, SearchOption.TopDirectoryOnly);

            if (wheelFiles.Length == 0)
            {
                return Result<string>.Fail($"No wheel files found for package '{packageName}' in '{folderPath}'");
            }

            if (wheelFiles.Length > 1)
            {
                var fileList = string.Join(", ", wheelFiles.Select(Path.GetFileName));
                return Result<string>.Fail($"Multiple wheel files found for package '{packageName}' in '{folderPath}': {fileList}");
            }

            return Result<string>.Ok(wheelFiles[0]);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Error searching for wheel files for package '{packageName}'")
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
