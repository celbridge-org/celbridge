using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
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
    private const string InstallLockFileName = "install.lock";
    private const string CelbridgeToolName = "celbridge";
    private const string CelbridgeToolCommand = "celbridge-py";
    private const string WheelFilePattern = "celbridge-*.whl";
    private const string PythonModuleFolder = "Celbridge.Python";
    private const string UVExecutableName = "uv";
    private const string UVExecutableNameWindows = "uv.exe";
    private const string PythonExecutableNameWindows = "python.exe";
    private const string PyvenvConfigFileName = "pyvenv.cfg";
    private const string PyvenvHomeKey = "home";

    // The bucket in uv's cache for the temporary environments it builds, such as the one each REPL runs in.
    private const string UvTemporaryEnvironmentsFolderName = "builds-v0";

    // Generous because the first install on a machine downloads an interpreter and the tool's packages
    // before it can publish anything.
    private static readonly TimeSpan ToolInstallTimeout = TimeSpan.FromMinutes(5);

    // The support folder is shared by every instance of the application, so waiting has to outlast another
    // instance's install rather than fail beside it.
    private static readonly TimeSpan InstallLockTimeout = TimeSpan.FromMinutes(6);

    // A retry of the lock starts soon and backs off, because another instance's install takes seconds.
    private static readonly TimeSpan InstallLockFirstRetryDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan InstallLockMaxRetryDelay = TimeSpan.FromSeconds(1);

    // A closed console's processes can hold their temporary environment's files for a moment, so removing
    // it is retried for a while.
    private static readonly TimeSpan TemporaryEnvironmentRemovalTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TemporaryEnvironmentRemovalRetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly ILocalFileSystem _fileSystem;
    private readonly ILogger<PythonInstaller> _logger;
    private readonly IAppEnvironment _appEnvironment;
    private readonly IPythonConfigService _pythonConfigService;

    private readonly SemaphoreSlim _installGate = new(1, 1);
    private Result? _installResult;
    private bool _hasListedStaleTemporaryEnvironments;

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

    public string UvExecutablePath => Path.Combine(
        UvBinFolderPath,
        OperatingSystem.IsWindows() ? UVExecutableNameWindows : UVExecutableName);

    private string UvToolsFolderPath => Path.Combine(PythonFolderPath, UvToolsFolderName);

    // uv's download cache and the interpreters it manages, shared by the installed tool and by every
    // project. Outside the Python folder, which a reinstall deletes wholesale, so a rebuilt wheel costs no
    // downloads. The installed tool runs on an interpreter from here, so an install is only current while
    // this folder still holds it.
    private string PythonCacheFolderPath => Path.Combine(_appEnvironment.LocalApplicationDataFolderPath, PythonCacheFolderName);

    public string UvCacheFolderPath => Path.Combine(PythonCacheFolderPath, UvCacheFolderName);

    public string UvPythonInstallFolderPath => Path.Combine(PythonCacheFolderPath, UvPythonInstallsFolderName);

    // The command the tool install publishes, and the tool environment's own interpreter. Both are checked
    // before an install is treated as current, because the marker describes what was installed and not what
    // survived.
    private string CelbridgeToolCommandPath => Path.Combine(
        UvToolBinFolderPath,
        OperatingSystem.IsWindows() ? $"{CelbridgeToolCommand}.exe" : CelbridgeToolCommand);

    private string CelbridgeToolInterpreterPath => OperatingSystem.IsWindows()
        ? Path.Combine(UvToolsFolderPath, CelbridgeToolName, "Scripts", "python.exe")
        : Path.Combine(UvToolsFolderPath, CelbridgeToolName, "bin", "python");

    public async Task<Result> InstallPythonAsync(string appVersion)
    {
        // Serialises callers within this process. A caller arriving during an install waits for it rather
        // than starting a second one, and the result is only reused while it still describes the disk.
        await _installGate.WaitAsync();
        try
        {
            if (_installResult is { IsSuccess: true } cachedResult &&
                await IsToolInstalledAsync())
            {
                return cachedResult;
            }

            _installResult = await RunInstallAsync(appVersion);
            return _installResult;
        }
        finally
        {
            _installGate.Release();
        }
    }

    public async Task<Result<string>> GetInstalledWheelPathAsync()
    {
        return await FindWheelFileAsync(PythonFolderPath);
    }

    public async Task RemoveTemporaryEnvironmentAsync(string folderPath)
    {
        var temporaryEnvironmentsPath = Path.GetFullPath(Path.Combine(UvCacheFolderPath, UvTemporaryEnvironmentsFolderName));
        var fullFolderPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
        var parentPath = Path.GetDirectoryName(fullFolderPath);
        if (!string.Equals(parentPath, temporaryEnvironmentsPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Left '{Path}' in place because it is not one of uv's temporary environments", folderPath);
            return;
        }

        var deadline = DateTime.UtcNow + TemporaryEnvironmentRemovalTimeout;
        while (true)
        {
            var infoResult = await _fileSystem.GetInfoAsync(fullFolderPath);
            if (infoResult.IsSuccess &&
                infoResult.Value.Kind == StorageItemKind.NotFound)
            {
                return;
            }

            var deleteResult = await _fileSystem.DeleteFolderAsync(fullFolderPath, recursive: true);
            if (deleteResult.IsSuccess)
            {
                _logger.LogDebug("Removed the temporary environment '{Path}'", fullFolderPath);
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                _logger.LogDebug("Failed to remove the temporary environment '{Path}': {Error}",
                    fullFolderPath,
                    deleteResult.FirstErrorMessage);
                return;
            }

            await Task.Delay(TemporaryEnvironmentRemovalRetryDelay);
        }
    }

    private async Task<Result> RunInstallAsync(string appVersion)
    {
        FileStream? installLock = null;
        try
        {
            var pythonFolderPath = PythonFolderPath;

            var versionContentResult = await GetVersionContentAsync(appVersion);
            if (versionContentResult.IsFailure)
            {
                return Result.Fail("Failed to describe the bundled Python support files")
                    .WithErrors(versionContentResult);
            }
            var versionContent = versionContentResult.Value;

            installLock = await AcquireInstallLockAsync();

            await BeginRemovingStaleTemporaryEnvironmentsAsync();

            if (await IsInstallCurrentAsync(pythonFolderPath, versionContent))
            {
                // The marker describes the folder, so only the tool can be missing, and republishing it
                // costs a fraction of a reinstall and destroys nothing a running console is using.
                if (await IsToolInstalledAsync())
                {
                    _logger.LogInformation("Python support files are current at {Path}", pythonFolderPath);
                    return Result.Ok();
                }

                _logger.LogInformation("Republishing the celbridge tool at {Path}", pythonFolderPath);
                await InstallCelbridgeToolAsync(pythonFolderPath);
                return Result.Ok();
            }

            _logger.LogInformation("Running full Python reinstall at {Path}", pythonFolderPath);
            await ReinstallAsync(pythonFolderPath, versionContent);
            _logger.LogInformation("Python reinstall completed");

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("Failed to install Python support files")
                .WithException(ex);
        }
        finally
        {
            installLock?.Dispose();
        }
    }

    // Held for the whole install because the folder is machine wide, and the in-process gate cannot see
    // another instance of the application. The handle is released by the operating system if this process
    // exits while holding it, so a crash does not strand the lock.
    private async Task<FileStream> AcquireInstallLockAsync()
    {
        var lockFilePath = Path.Combine(_appEnvironment.LocalApplicationDataFolderPath, InstallLockFileName);
        await _fileSystem.CreateFolderAsync(_appEnvironment.LocalApplicationDataFolderPath);

        var waitTimer = Stopwatch.StartNew();
        var deadline = DateTime.UtcNow + InstallLockTimeout;
        var retryDelay = InstallLockFirstRetryDelay;
        var hasWaited = false;
        while (true)
        {
            try
            {
                var installLock = new FileStream(
                    lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

                if (hasWaited)
                {
                    _logger.LogInformation("Waited {DurationMs}ms for another Celbridge instance to finish installing Python",
                        waitTimer.ElapsedMilliseconds);
                }

                return installLock;
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                if (!hasWaited)
                {
                    _logger.LogInformation("Waiting for another Celbridge instance to finish installing Python");
                    hasWaited = true;
                }

                await Task.Delay(retryDelay);

                var doubledDelay = retryDelay * 2;
                retryDelay = doubledDelay < InstallLockMaxRetryDelay ? doubledDelay : InstallLockMaxRetryDelay;
            }
        }
    }

    // uv removes a REPL's temporary environment when the REPL exits normally, and one whose console was torn
    // down can leave it behind. The first install of a launch runs before any console can start, so what it
    // lists belongs to earlier sessions and is removed in the background. Another instance's REPLs use the
    // same cache, so nothing is listed while one is running.
    private async Task BeginRemovingStaleTemporaryEnvironmentsAsync()
    {
        if (_hasListedStaleTemporaryEnvironments)
        {
            return;
        }
        _hasListedStaleTemporaryEnvironments = true;

        try
        {
            if (IsAnotherInstanceRunning())
            {
                _logger.LogDebug("Left the temporary environments in the uv cache in place because another Celbridge instance is running");
                return;
            }

            var temporaryEnvironmentsPath = Path.Combine(UvCacheFolderPath, UvTemporaryEnvironmentsFolderName);
            var enumerateResult = await _fileSystem.EnumerateAsync(temporaryEnvironmentsPath, "*", recursive: false);
            if (enumerateResult.IsFailure)
            {
                // A cache that has never built an environment has no folder to list.
                return;
            }

            var staleFolderPaths = enumerateResult.Value
                .Where(entry => entry.IsFolder)
                .Select(entry => entry.FullPath)
                .ToList();

            if (staleFolderPaths.Count > 0)
            {
                _ = Task.Run(() => RemoveTemporaryEnvironmentsAsync(staleFolderPaths));
            }
        }
        catch (Exception exception)
        {
            // The stale environments stay until a later launch, which costs disk and nothing else.
            _logger.LogWarning(exception, "Failed to list the temporary environments in the uv cache");
        }
    }

    private async Task RemoveTemporaryEnvironmentsAsync(IReadOnlyList<string> folderPaths)
    {
        var removalTimer = Stopwatch.StartNew();
        var removedCount = 0;
        foreach (var folderPath in folderPaths)
        {
            var deleteResult = await _fileSystem.DeleteFolderAsync(folderPath, recursive: true);
            if (deleteResult.IsFailure)
            {
                _logger.LogDebug("Failed to remove the temporary environment '{Path}': {Error}",
                    folderPath,
                    deleteResult.FirstErrorMessage);
                continue;
            }

            removedCount++;
        }

        _logger.LogInformation("Removed {RemovedCount} of {Count} temporary environments left in the uv cache in {DurationMs}ms",
            removedCount,
            folderPaths.Count,
            removalTimer.ElapsedMilliseconds);
    }

    internal static bool IsAnotherInstanceRunning()
    {
        using var currentProcess = Process.GetCurrentProcess();
        var namesakes = Process.GetProcessesByName(currentProcess.ProcessName);
        try
        {
            return namesakes.Any(process => process.Id != currentProcess.Id);
        }
        finally
        {
            foreach (var process in namesakes)
            {
                process.Dispose();
            }
        }
    }

    private async Task<bool> IsInstallCurrentAsync(string pythonFolderPath, string versionContent)
    {
        var pythonFolderInfoResult = await _fileSystem.GetInfoAsync(pythonFolderPath);
        bool pythonFolderExists = pythonFolderInfoResult.IsSuccess
            && pythonFolderInfoResult.Value.Kind == StorageItemKind.Folder;
        if (!pythonFolderExists)
        {
            _logger.LogDebug("Python reinstall required: pythonFolder does not exist at {Path}", pythonFolderPath);
            return false;
        }

        var installedVersionPath = Path.Combine(pythonFolderPath, InstalledVersionFileName);
        var installedVersionInfoResult = await _fileSystem.GetInfoAsync(installedVersionPath);
        bool installedVersionExists = installedVersionInfoResult.IsSuccess
            && installedVersionInfoResult.Value.Kind == StorageItemKind.File;
        if (!installedVersionExists)
        {
            _logger.LogDebug("Python reinstall required: installed_version.txt missing at {Path}", installedVersionPath);
            return false;
        }

        var readResult = await _fileSystem.ReadAllTextAsync(installedVersionPath);
        if (readResult.IsFailure)
        {
            _logger.LogDebug("Python reinstall required: installed_version.txt unreadable at {Path}", installedVersionPath);
            return false;
        }

        // Both sides are trimmed, so a marker that gained or lost a trailing newline on the way to disk
        // does not read as a changed install.
        var installedVersionContent = readResult.Value.Trim();
        var expectedVersionContent = versionContent.Trim();

        if (!string.Equals(expectedVersionContent, installedVersionContent, StringComparison.Ordinal))
        {
            _logger.LogDebug(
                "Python reinstall required: installed_version.txt mismatch. Installed='{Installed}' Expected='{Expected}'",
                installedVersionContent.Replace("\n", "\\n"),
                expectedVersionContent.Replace("\n", "\\n"));
            return false;
        }

        return true;
    }

    // uv, the published command, the tool environment's own interpreter, and the interpreter in the shared
    // cache folder that the environment runs on. No install describes the shared folder, so its interpreter
    // can go while the marker still matches.
    private async Task<bool> IsToolInstalledAsync()
    {
        foreach (var path in new[] { UvExecutablePath, CelbridgeToolCommandPath, CelbridgeToolInterpreterPath })
        {
            if (!await IsToolPartPresentAsync(path, StorageItemKind.File))
            {
                return false;
            }
        }

        var interpreterHomePath = await ReadToolInterpreterHomeAsync();
        if (string.IsNullOrEmpty(interpreterHomePath))
        {
            _logger.LogDebug("The celbridge tool is not installed: its environment names no interpreter");
            return false;
        }

        // On Windows the environment's own interpreter is a launcher that outlives the interpreter it starts,
        // and a junction whose target has gone still reads as a folder, so the interpreter itself is checked.
        if (OperatingSystem.IsWindows())
        {
            var interpreterPath = Path.Combine(interpreterHomePath, PythonExecutableNameWindows);
            return await IsToolPartPresentAsync(interpreterPath, StorageItemKind.File);
        }

        return await IsToolPartPresentAsync(interpreterHomePath, StorageItemKind.Folder);
    }

    private async Task<bool> IsToolPartPresentAsync(string path, StorageItemKind expectedKind)
    {
        var infoResult = await _fileSystem.GetInfoAsync(path);
        if (infoResult.IsFailure ||
            infoResult.Value.Kind != expectedKind)
        {
            _logger.LogDebug("The celbridge tool is not installed: '{Path}' is missing", path);
            return false;
        }

        return true;
    }

    // The folder holding the interpreter the tool environment runs on, as its pyvenv.cfg names it.
    private async Task<string?> ReadToolInterpreterHomeAsync()
    {
        var configPath = Path.Combine(UvToolsFolderPath, CelbridgeToolName, PyvenvConfigFileName);
        var readResult = await _fileSystem.ReadAllTextAsync(configPath);
        if (readResult.IsFailure)
        {
            return null;
        }
        var configText = readResult.Value;

        return ReadPyvenvHome(configText);
    }

    // The value of the home key in the text of a pyvenv.cfg, or null when it has none.
    internal static string? ReadPyvenvHome(string configText)
    {
        foreach (var line in configText.Split('\n'))
        {
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            if (!key.Equals(PyvenvHomeKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return line[(separatorIndex + 1)..].Trim();
        }

        return null;
    }

    // Everything the installed folder is built from: the application version, the wheel, the interpreter
    // version the tool is pinned to, and the version of the bundled uv. An input the marker leaves out is
    // an input a changed build never applies.
    private async Task<Result<string>> GetVersionContentAsync(string appVersion)
    {
        var assetsFolder = _appEnvironment.GetBundledAssetPath(PythonModuleFolder, "Assets/Python");

        var findWheelResult = await FindWheelFileAsync(assetsFolder);
        if (findWheelResult.IsFailure)
        {
            return Result<string>.Fail("Failed to find the bundled celbridge wheel")
                .WithErrors(findWheelResult);
        }
        var bundledWheelPath = findWheelResult.Value;

        var wheelHash = await FileHashHelper.HashFileContentsAsync(bundledWheelPath);
        if (string.IsNullOrEmpty(wheelHash))
        {
            return Result<string>.Fail($"Failed to hash the bundled celbridge wheel '{bundledWheelPath}'");
        }

        var uvVersion = await ReadBundledUvVersionAsync();
        var pythonVersion = _pythonConfigService.DefaultPythonVersion;

        return Result<string>.Ok($"{appVersion}\n{wheelHash}\n{pythonVersion}\n{uvVersion}");
    }

    private async Task<string> ReadBundledUvVersionAsync()
    {
        var uvVersionPath = _appEnvironment.GetBundledAssetPath(PythonModuleFolder, "Assets/UV/uv_version.txt");
        var readResult = await _fileSystem.ReadAllTextAsync(uvVersionPath);
        if (readResult.IsFailure)
        {
            return string.Empty;
        }

        return readResult.Value.Trim();
    }

    private async Task ReinstallAsync(string pythonFolderPath, string versionContent)
    {
        // Resolved before anything is deleted, so a build that shipped without its assets fails with the
        // working install still in place rather than after it has been destroyed.
        var findArchiveResult = await FindUvArchiveAsync();
        if (findArchiveResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Python cannot be installed: {findArchiveResult.FirstErrorMessage}");
        }
        var uvArchivePath = findArchiveResult.Value;

        var pythonAssetsPath = _appEnvironment.GetBundledAssetPath(PythonModuleFolder, "Assets/Python");
        var findWheelResult = await FindWheelFileAsync(pythonAssetsPath);
        if (findWheelResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"The bundled celbridge wheel is missing from '{pythonAssetsPath}', so Python cannot be installed.");
        }

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

        await ExtractUvArchiveAsync(uvArchivePath, UvBinFolderPath);
        await CopyBundledFolderAsync(pythonAssetsPath, pythonFolderPath);

        await InstallCelbridgeToolAsync(pythonFolderPath);

        // Written last, so an install that stopped part way leaves no marker and the next launch rebuilds.
        var versionFile = Path.Combine(pythonFolderPath, InstalledVersionFileName);
        await _fileSystem.WriteAllTextAsync(versionFile, versionContent);
    }

    // Publishes the celbridge-py command from the wheel in the support folder. One environment for the
    // application, because it only runs the bootstrap shim and the interpreter and packages the REPL
    // imports come from the inner uv run that shim performs.
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

        // uv rejects --managed-python outright when this is set, and the child inherits the environment
        // this process was launched with. The bootstrap shim drops it for the same reason.
        processStartInfo.Environment.Remove("UV_PYTHON_PREFERENCE");

        var installTimer = Stopwatch.StartNew();

        using var process = Process.Start(processStartInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"Failed to start uv at '{uvExePath}'");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCancellation = new CancellationTokenSource(ToolInstallTimeout);
        bool timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                // Racing the process exiting on its own, which leaves nothing to kill.
                _logger.LogDebug(ex, "Failed to kill the timed out uv tool install");
            }
        }

        // Read whatever uv produced either way, because a timeout is the failure whose output explains
        // most and the readers have to be drained before the process is disposed.
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (timedOut)
        {
            _logger.LogError("uv tool install timed out after {Timeout} minutes. Stderr: {Stderr}. Stdout: {Stdout}",
                ToolInstallTimeout.TotalMinutes, stderr, stdout);

            throw new InvalidOperationException(
                $"uv tool install timed out after {ToolInstallTimeout.TotalMinutes} minutes. {stderr.Trim()}");
        }

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
        var entries = enumerateFilesResult.Value;

        var wheelFile = entries.FirstOrDefault(entry => !entry.IsFolder);
        if (wheelFile is null)
        {
            return Result<string>.Fail($"No celbridge wheel found in '{folderPath}'");
        }

        return Result<string>.Ok(wheelFile.FullPath);
    }

    // The build downloads one archive, for the platform it is building for, so the bundled folder holds
    // exactly one. Reading the name off disk keeps the platform table in the build alone, where a uv
    // release that renames a triple is a build failure rather than a first-launch one.
    private async Task<Result<string>> FindUvArchiveAsync()
    {
        var uvAssetFolder = _appEnvironment.GetBundledAssetPath(PythonModuleFolder, "Assets/UV");

        var enumerateResult = await _fileSystem.EnumerateAsync(uvAssetFolder, "uv-*", recursive: false);
        if (enumerateResult.IsFailure)
        {
            return Result<string>.Fail($"Error searching for the bundled uv archive in '{uvAssetFolder}'")
                .WithErrors(enumerateResult);
        }
        var entries = enumerateResult.Value;

        var archive = entries.FirstOrDefault(entry => !entry.IsFolder);
        if (archive is null)
        {
            return Result<string>.Fail($"No bundled uv archive found in '{uvAssetFolder}'");
        }

        return Result<string>.Ok(archive.FullPath);
    }

    // Extracts the bundled uv archive into the uv bin folder. uv ships Windows as a .zip with the
    // binaries at the root, and macOS and Linux as a .tar.gz whose binaries sit under a single top-level
    // folder (e.g. uv-aarch64-apple-darwin/uv). That folder is stripped so the binaries land directly in
    // the bin folder, which carries executables alone so that putting it on a console PATH exposes
    // nothing else. TarFile preserves the Unix executable mode and the flattening move is a rename that
    // preserves it, so no explicit chmod is needed.
    private async Task ExtractUvArchiveAsync(string uvArchivePath, string uvBinFolderPath)
    {
        await _fileSystem.CreateFolderAsync(uvBinFolderPath);

        if (uvArchivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
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
