using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Celbridge.Platform;
using Celbridge.FileSystem;
using Celbridge.Logging;
using Celbridge.Utilities;

namespace Celbridge.Python.Services;

public class PythonInstaller : IPythonInstaller
{
    private const string PythonFolderName = "Python";
    private const string PythonCacheFolderName = "PythonCache";
    private const string UvBinFolderName = "bin";
    private const string UvToolsFolderName = "uv_tools";
    private const string UvToolBinFolderName = "uv_bin";
    private const string UvCacheFolderName = "uv_cache";
    private const string UvPythonInstallsFolderName = "uv_python_installs";
    private const string InstalledVersionFileName = "installed_version.txt";
    private const string WheelFilePattern = "celbridge-*.whl";
    private const string PythonModuleFolder = "Celbridge.Python";
    private const string UVExecutableName = "uv";
    private const string UVExecutableNameWindows = "uv.exe";

    // Generous because the first install on a machine downloads an interpreter and the tool's packages
    // before it can publish anything.
    private static readonly TimeSpan ToolInstallTimeout = TimeSpan.FromMinutes(5);

    private readonly ILocalFileSystem _fileSystem;
    private readonly ILogger<PythonInstaller> _logger;
    private readonly IAppEnvironment _appEnvironment;
    private readonly IPythonConfigService _pythonConfigService;

    private readonly object _installLock = new();
    private Task<Result<string>>? _installTask;

    public PythonInstaller(
        ILocalFileSystem fileSystem,
        ILogger<PythonInstaller> logger,
        IAppEnvironment appEnvironment,
        IPythonConfigService pythonConfigService)
    {
        _fileSystem = fileSystem;
        _logger = logger;
        _appEnvironment = appEnvironment;
        _pythonConfigService = pythonConfigService;
    }

    public string PythonFolderPath => Path.Combine(_appEnvironment.LocalApplicationDataFolderPath, PythonFolderName);

    public string UvBinFolderPath => Path.Combine(PythonFolderPath, UvBinFolderName);

    public string UvToolBinFolderPath => Path.Combine(PythonFolderPath, UvToolBinFolderName);

    private string UvToolsFolderPath => Path.Combine(PythonFolderPath, UvToolsFolderName);

    // uv's download cache and the interpreters it manages, shared by the installed tool and by every
    // project. Outside the Python folder, which a reinstall deletes wholesale: nothing here is described
    // by an install, so keeping it means a rebuilt wheel costs no downloads.
    private string PythonCacheFolderPath => Path.Combine(_appEnvironment.LocalApplicationDataFolderPath, PythonCacheFolderName);

    public string UvCacheFolderPath => Path.Combine(PythonCacheFolderPath, UvCacheFolderName);

    public string UvPythonInstallFolderPath => Path.Combine(PythonCacheFolderPath, UvPythonInstallsFolderName);

    private string UvExecutablePath => Path.Combine(
        UvBinFolderPath,
        OperatingSystem.IsWindows() ? UVExecutableNameWindows : UVExecutableName);

    public Task<Result<string>> InstallPythonAsync(string appVersion)
    {
        // A reinstall deletes and re-extracts one shared app-data folder, so concurrent callers must share a
        // single run rather than each deleting the folder the others are extracting into. Consoles start
        // together, so this is reached concurrently whenever a project has several of them open.
        lock (_installLock)
        {
            if (ShouldStartInstall())
            {
                _installTask = RunInstallAsync(appVersion);
            }

            return _installTask!;
        }
    }

    // A failed install is not cached, so the next console to start retries rather than inheriting it.
    private bool ShouldStartInstall()
    {
        if (_installTask is null)
        {
            return true;
        }

        if (!_installTask.IsCompleted)
        {
            return false;
        }

        if (_installTask.IsFaulted)
        {
            return true;
        }

        return _installTask.Result.IsFailure;
    }

    private async Task<Result<string>> RunInstallAsync(string appVersion)
    {
        try
        {
            var pythonFolderPath = PythonFolderPath;

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
        var assetsFolder = _appEnvironment.GetBundledAssetPath(PythonModuleFolder, "Assets/Python");
        var findWheelResult = await FindWheelFileAsync(assetsFolder);
        if (findWheelResult.IsSuccess)
        {
            wheelHash = await FileHashHelper.HashFileContentsAsync(findWheelResult.Value);
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

        // Bundled assets are read as real files from the Celbridge.Python module folder beside the app.
        // uv handles installing the required python & package versions for the loaded project.
        var uvArchivePath = _appEnvironment.GetBundledAssetPath(
            PythonModuleFolder, $"Assets/UV/{GetUvArchiveFileName()}");
        await ExtractUvArchiveAsync(uvArchivePath, UvBinFolderPath);

        // Copy the bundled Python assets to the local Python folder.
        var pythonAssetsPath = _appEnvironment.GetBundledAssetPath(PythonModuleFolder, "Assets/Python");
        await CopyBundledFolderAsync(pythonAssetsPath, pythonFolderPath);

        await InstallCelbridgeToolAsync(pythonFolderPath);

        // Write the version file after successful install.
        // This signals that the install completed successfully and includes both the app
        // version and the build version so that changes to either trigger a reinstall.
        var versionFile = Path.Combine(pythonFolderPath, InstalledVersionFileName);
        var versionContent = await GetVersionContentAsync(currentVersion);
        await _fileSystem.WriteAllTextAsync(versionFile, versionContent);
    }

    // Publishes the celbridge-py command from the wheel that was just copied in. One environment for the
    // application: it only runs the bootstrap shim, and the interpreter and packages the REPL imports come
    // from the inner uv run that shim performs, so nothing here varies by project.
    //
    // A fixed interpreter version, so the version marker fully describes what is installed. The version the
    // REPL runs on is chosen by that inner uv run.
    private async Task InstallCelbridgeToolAsync(string pythonFolderPath)
    {
        var findWheelResult = await FindWheelFileAsync(pythonFolderPath);
        if (findWheelResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to find the celbridge wheel to install as a tool: {findWheelResult.FirstErrorMessage}");
        }
        var celbridgeWheelPath = findWheelResult.Value;

        var pythonVersion = _pythonConfigService.DefaultPythonVersion;

        _logger.LogInformation("Installing celbridge as a uv tool with Python {PythonVersion}", pythonVersion);

        var uvExePath = UvExecutablePath;
        var processStartInfo = new ProcessStartInfo
        {
            FileName = uvExePath,
            WorkingDirectory = UvBinFolderPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var toolInstallArguments = new[]
        {
            "tool",
            "install",
            "--force",
            "--python", pythonVersion,
            "--managed-python",
            celbridgeWheelPath,
        };
        foreach (var argument in toolInstallArguments)
        {
            processStartInfo.ArgumentList.Add(argument);
        }

        _logger.LogDebug("uv tool install command: {FileName} {Arguments}", uvExePath, string.Join(' ', toolInstallArguments));

        processStartInfo.Environment["UV_TOOL_DIR"] = UvToolsFolderPath;
        processStartInfo.Environment["UV_TOOL_BIN_DIR"] = UvToolBinFolderPath;
        processStartInfo.Environment["UV_PYTHON_INSTALL_DIR"] = UvPythonInstallFolderPath;
        processStartInfo.Environment["UV_CACHE_DIR"] = UvCacheFolderPath;

        var installTimer = Stopwatch.StartNew();

        using var process = Process.Start(processStartInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"Failed to start uv at '{uvExePath}'");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCancellation = new CancellationTokenSource(ToolInstallTimeout);
        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);

            throw new InvalidOperationException(
                $"uv tool install timed out after {ToolInstallTimeout.TotalMinutes} minutes");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        // Throws rather than leaving the marker unwritten and the folder half-built, so the failure reads
        // as an install failure instead of an unrelated Python traceback at the first console launch.
        if (process.ExitCode != 0)
        {
            _logger.LogError("uv tool install exited with code {ExitCode} after {DurationMs}ms. Stderr: {Stderr}. Stdout: {Stdout}",
                process.ExitCode, installTimer.ElapsedMilliseconds, stderr, stdout);

            throw new InvalidOperationException(
                $"uv tool install exited with code {process.ExitCode}. {stderr.Trim()}");
        }

        _logger.LogInformation("celbridge tool installed successfully in {DurationMs}ms", installTimer.ElapsedMilliseconds);
    }

    private async Task<Result<string>> FindWheelFileAsync(string folderPath)
    {
        var enumerateFilesResult = await _fileSystem.EnumerateAsync(folderPath, WheelFilePattern, recursive: false);
        if (enumerateFilesResult.IsFailure)
        {
            return Result<string>.Fail($"Error searching for the celbridge wheel in '{folderPath}'")
                .WithErrors(enumerateFilesResult);
        }

        var wheelFile = enumerateFilesResult.Value.FirstOrDefault(entry => !entry.IsFolder);
        if (wheelFile is null)
        {
            return Result<string>.Fail($"No celbridge wheel found in '{folderPath}'");
        }

        return Result<string>.Ok(wheelFile.FullPath);
    }

    // Returns the uv release archive filename for the running OS and architecture, matching the DownloadUv
    // MSBuild target in Celbridge.Python.csproj. Windows ships a .zip with the binaries at the root. macOS
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

    // Extracts the bundled uv archive into the uv bin folder. On Windows (the Skia desktop head can run
    // there too) the archive is a .zip with the binaries at the root. On macOS and Linux it is a .tar.gz
    // whose binaries sit under a single top-level folder (e.g. uv-aarch64-apple-darwin/uv). That folder is
    // stripped so the binaries land directly in the bin folder, which carries executables alone so that
    // putting it on a console PATH exposes nothing else. TarFile preserves the Unix executable mode and
    // the flattening move is a rename that preserves it, so no explicit chmod is needed.
    private async Task ExtractUvArchiveAsync(string uvArchivePath, string uvBinFolderPath)
    {
        await _fileSystem.CreateFolderAsync(uvBinFolderPath);

        if (OperatingSystem.IsWindows())
        {
            ZipFile.ExtractToDirectory(uvArchivePath, uvBinFolderPath, overwriteFiles: true);
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
            TarFile.ExtractToDirectory(gzipStream, uvBinFolderPath, overwriteFiles: true);
        }

        // The tarball extracts a single top-level folder named after the archive (without the .tar.gz
        // suffix). Move its files up so the uv binary sits directly in the bin folder.
        var topLevelFolderName = Path.GetFileName(uvArchivePath).Replace(".tar.gz", string.Empty);
        var extractedFolder = Path.Combine(uvBinFolderPath, topLevelFolderName);

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

            var destPath = Path.Combine(uvBinFolderPath, Path.GetFileName(entry.FullPath));
            var moveResult = await _fileSystem.MoveFileAsync(entry.FullPath, destPath);
            if (moveResult.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Failed to move uv binary '{entry.FullPath}' to '{destPath}': {moveResult.FirstErrorMessage}");
            }
        }

        await _fileSystem.DeleteFolderAsync(extractedFolder, recursive: true);

        await RemoveAppleDoubleFilesAsync(uvBinFolderPath);
    }

    // The uv tarball carries an AppleDouble stub beside each entry, which TarFile writes out as a file with
    // the executable mode of the entry it describes. The bin folder is on a console PATH, so it holds the
    // binaries alone.
    private async Task RemoveAppleDoubleFilesAsync(string uvBinFolderPath)
    {
        var enumerateResult = await _fileSystem.EnumerateAsync(uvBinFolderPath, "._*", recursive: false);
        if (enumerateResult.IsFailure)
        {
            return;
        }

        foreach (var entry in enumerateResult.Value)
        {
            if (entry.IsFolder)
            {
                continue;
            }

            await _fileSystem.DeleteFileAsync(entry.FullPath);
        }
    }

    // Recursively copies a bundled-asset folder to a destination through the filesystem gateway.
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
}
