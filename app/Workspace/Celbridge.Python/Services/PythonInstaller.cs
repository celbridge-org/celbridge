using System.IO.Compression;
using System.Diagnostics;

namespace Celbridge.Python.Services;

public static class PythonInstaller
{
    private const string PythonFolderName = "Python";
    private const string PythonAssetsFolder = "Assets\\Python";
    private const string UVZipAssetPath = "ms-appx:///Assets/UV/uv-x86_64-pc-windows-msvc.zip";
    private const string InstalledVersionFileName = "installed_version.txt";
    private const string UVToolsSubfolder = "uv_tools";
    private const string UVBinSubfolder = "uv_bin";
    private const string UVPythonInstallsSubfolder = "uv_python_installs";
    private const string UVExecutableName = "uv.exe";
    private const string UVTempFileName = "uv.zip";

    /// <summary>
    /// Installs Python support files if needed.
    /// </summary>
    public static async Task<Result<string>> InstallPythonAsync(string appVersion)
    {
        try
        {
            var localFolder = ApplicationData.Current.LocalFolder;
            var pythonFolderPath = Path.Combine(localFolder.Path, PythonFolderName);

            bool needsReinstall = IsInstallRequired(pythonFolderPath, appVersion);

            if (needsReinstall)
            {
                await ReinstallAsync(localFolder, pythonFolderPath, appVersion);
            }

            return Result<string>.Ok(pythonFolderPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to install Python support files")
                .WithException(ex);
        }
    }

    private static bool IsInstallRequired(string pythonFolderPath, string currentVersion)
    {
        // If the python folder doesn't exist, we need to install
        if (!Directory.Exists(pythonFolderPath))
        {
            return true;
        }

        var installedVersionPath = Path.Combine(pythonFolderPath, InstalledVersionFileName);

        // If version file doesn't exist, we need to install
        if (!File.Exists(installedVersionPath))
        {
            return true;
        }

        // Read the installed version
        var installedVersion = File.ReadAllText(installedVersionPath).Trim();

        // If versions don't match, we need to reinstall
        if (!string.Equals(currentVersion, installedVersion, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static async Task ReinstallAsync(StorageFolder localFolder, string pythonFolderPath, string currentVersion)
    {
        // Delete existing folder if it exists (handles upgrade scenario)
        // Use retry logic as files may be locked by a previous Python process
        if (Directory.Exists(pythonFolderPath))
        {
            await DeleteDirectoryWithRetryAsync(pythonFolderPath);
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

        // Install the celbridge package as a tool using uv
        await InstallCelbridgeToolAsync(pythonFolderPath);

        // Write the version file after successful install
        // This signals that the install completed successfully.
        var versionFile = Path.Combine(pythonFolderPath, InstalledVersionFileName);
        await File.WriteAllTextAsync(versionFile, currentVersion);
    }

    private static async Task InstallCelbridgeToolAsync(string pythonFolderPath)
    {
        // Create directories for uv tools, binaries, and Python installations
        var uvToolDir = Path.Combine(pythonFolderPath, UVToolsSubfolder);
        var uvToolBinDir = Path.Combine(pythonFolderPath, UVBinSubfolder);
        var uvPythonInstallDir = Path.Combine(pythonFolderPath, UVPythonInstallsSubfolder);
        Directory.CreateDirectory(uvToolDir);
        Directory.CreateDirectory(uvToolBinDir);
        Directory.CreateDirectory(uvPythonInstallDir);

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
        // Use --managed-python to only use uv-managed Python, ignoring system Python
        var processStartInfo = new ProcessStartInfo
        {
            FileName = uvExePath,
            Arguments = $"tool install --force --managed-python \"{celbridgeWheelPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = pythonFolderPath
        };

        // Set environment variables to override uv's default locations
        processStartInfo.EnvironmentVariables["UV_TOOL_DIR"] = uvToolDir;
        processStartInfo.EnvironmentVariables["UV_TOOL_BIN_DIR"] = uvToolBinDir;
        processStartInfo.EnvironmentVariables["UV_PYTHON_INSTALL_DIR"] = uvPythonInstallDir;

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

    /// <summary>
    /// Deletes a directory with retry logic to handle locked files.
    /// Files may be locked by a previous Python process that hasn't fully exited yet.
    /// </summary>
    private static async Task DeleteDirectoryWithRetryAsync(string directoryPath, int maxRetries = 5, int delayMs = 500)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                Directory.Delete(directoryPath, recursive: true);
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < maxRetries)
            {
                // Files may be locked - wait and retry
                await Task.Delay(delayMs * attempt);
            }
            catch (IOException) when (attempt < maxRetries)
            {
                // Files may be in use - wait and retry
                await Task.Delay(delayMs * attempt);
            }
        }

        // Final attempt without catching - let it throw if it fails
        Directory.Delete(directoryPath, recursive: true);
    }
}
