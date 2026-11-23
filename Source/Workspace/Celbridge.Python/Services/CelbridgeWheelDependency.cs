using System.Diagnostics;
using Celbridge.Automation;
using Celbridge.Dependencies;
using Celbridge.FileSystem;
using Celbridge.Localization;
using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.Utilities;

namespace Celbridge.Python.Services;

/// <summary>
/// Checks and installs the celbridge wheel in the dependencies folder. Installing copies the wheel and the
/// pinned interpreter version from the bundled assets. Then uv publishes the celbridge-py tool and fetches the tool's
/// interpreter.
/// </summary>
public sealed class CelbridgeWheelDependency : IDependency
{
    private const string WheelFilePattern = "celbridge-*.whl";
    private const string PythonModuleFolder = "Celbridge.Python";

    // The timeout is generous. The first install on a machine downloads an interpreter and the tool's packages
    // before publishing anything.
    private static readonly TimeSpan ToolInstallTimeout = TimeSpan.FromMinutes(5);

    // Every instance of the application shares the Python folder. The wait for the lock outlasts another
    // instance's install.
    private static readonly TimeSpan InstallLockTimeout = TimeSpan.FromMinutes(6);

    // A retry of the lock starts soon and backs off, because another instance's install takes seconds.
    private static readonly TimeSpan InstallLockFirstRetryDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan InstallLockMaxRetryDelay = TimeSpan.FromSeconds(1);

    private readonly ILocalFileSystem _fileSystem;
    private readonly ILogger<CelbridgeWheelDependency> _logger;
    private readonly IAppEnvironment _appEnvironment;
    private readonly IPythonConfigService _pythonConfigService;
    private readonly IAutomationService _automationService;
    private readonly ILocalizerService _localizerService;
    private readonly UvDependency _uvDependency;
    private readonly IUvRunner _uvRunner;
    private readonly CelbridgeWheelFolders _folders;

    public CelbridgeWheelDependency(
        ILocalFileSystem fileSystem,
        ILogger<CelbridgeWheelDependency> logger,
        IAppEnvironment appEnvironment,
        IPythonConfigService pythonConfigService,
        IAutomationService automationService,
        ILocalizerService localizerService,
        UvDependency uvDependency,
        IUvRunner uvRunner,
        CelbridgeWheelFolders folders)
    {
        _fileSystem = fileSystem;
        _logger = logger;
        _appEnvironment = appEnvironment;
        _pythonConfigService = pythonConfigService;
        _automationService = automationService;
        _localizerService = localizerService;
        _uvDependency = uvDependency;
        _uvRunner = uvRunner;
        _folders = folders;
    }

    public string Id => "celbridge-wheel";

    public string Version => _pythonConfigService.DefaultPythonVersion;

    public string Name => _localizerService.GetString("Dependency_CelbridgeWheel_Name");

    public string ProgressTitle => _localizerService.GetString("Dependency_CelbridgeWheel_ProgressTitle");

    private string BundledAssetsFolderPath => _appEnvironment.GetBundledAssetPath(PythonModuleFolder, "Assets/Python");

    public async Task<bool> IsInstalledAsync()
    {
        var appVersion = _appEnvironment.GetEnvironmentInfo().AppVersion;

        var versionContentResult = await GetVersionContentAsync(appVersion);
        if (versionContentResult.IsFailure)
        {
            _logger.LogWarning(versionContentResult, "Failed to describe the bundled celbridge wheel");
            return false;
        }
        var versionContent = versionContentResult.Value;

        return await IsInstallCurrentAsync(versionContent)
            && await IsToolInstalledAsync();
    }

    public async Task<Result> InstallAsync()
    {
        try
        {
            var appVersion = _appEnvironment.GetEnvironmentInfo().AppVersion;

            var versionContentResult = await GetVersionContentAsync(appVersion);
            if (versionContentResult.IsFailure)
            {
                return Result.Fail("Failed to describe the bundled celbridge wheel")
                    .WithErrors(versionContentResult);
            }
            var versionContent = versionContentResult.Value;

            var lockResult = await AcquireInstallLockAsync();
            if (lockResult.IsFailure)
            {
                return Result.Fail("Failed to take the Python install lock")
                    .WithErrors(lockResult);
            }
            using var installLock = lockResult.Value;

            var pythonFolderPath = _folders.PythonFolderPath;
            if (await IsInstallCurrentAsync(versionContent))
            {
                // The marker describes the folder, so only the tool can be missing. Republishing the tool costs a
                // fraction of a reinstall, and leaves everything a running console uses in place.
                if (await IsToolInstalledAsync())
                {
                    _logger.LogInformation("The celbridge wheel is current at {Path}", pythonFolderPath);
                    _automationService.LogEvent(new CelbridgeWheelCurrentEvent());
                    return Result.Ok();
                }

                _logger.LogInformation("Republishing the celbridge tool at {Path}", pythonFolderPath);
                _automationService.LogEvent(new PythonToolRepublishingEvent());
                return await InstallToolAsync();
            }

            _logger.LogInformation("Running full Python reinstall at {Path}", pythonFolderPath);
            _automationService.LogEvent(new PythonReinstallStartedEvent());

            var reinstallResult = await ReinstallAsync(versionContent);
            if (reinstallResult.IsFailure)
            {
                return reinstallResult;
            }

            _logger.LogInformation("Python reinstall completed");
            _automationService.LogEvent(new PythonReinstallCompletedEvent());

            return Result.Ok();
        }
        catch (Exception exception)
        {
            return Result.Fail("Failed to install the celbridge wheel")
                .WithException(exception);
        }
    }

    /// <summary>
    /// Returns the absolute path of the installed celbridge wheel. Fails until an install completes.
    /// </summary>
    public async Task<Result<string>> GetInstalledWheelPathAsync()
    {
        return await FindWheelFileAsync(_folders.PythonFolderPath);
    }

    // Every instance of the application shares the Python folder, so the lock is held for the whole install. The
    // operating system releases the handle when this process exits, so a crash leaves the lock free.
    private async Task<Result<FileStream>> AcquireInstallLockAsync()
    {
        var lockFilePath = _folders.InstallLockFilePath;
        var lockFolderPath = Path.GetDirectoryName(lockFilePath)!;
        var createResult = await _fileSystem.CreateFolderAsync(lockFolderPath);
        if (createResult.IsFailure)
        {
            return Result<FileStream>.Fail($"Failed to create the folder of the install lock '{lockFolderPath}'")
                .WithErrors(createResult);
        }

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
                    _automationService.LogEvent(new PythonInstallWaitedEvent(waitTimer.ElapsedMilliseconds));
                }

                return Result<FileStream>.Ok(installLock);
            }
            catch (IOException exception)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    return Result<FileStream>.Fail($"The install lock stayed unavailable for {InstallLockTimeout.TotalMinutes} minutes")
                        .WithException(exception);
                }

                if (!hasWaited)
                {
                    _logger.LogInformation("Waiting for another Celbridge instance to finish installing Python");
                    _automationService.LogEvent(new PythonInstallWaitingEvent());
                    hasWaited = true;
                }

                await Task.Delay(retryDelay);

                var doubledDelay = retryDelay * 2;
                retryDelay = doubledDelay < InstallLockMaxRetryDelay ? doubledDelay : InstallLockMaxRetryDelay;
            }
        }
    }

    private async Task<bool> IsInstallCurrentAsync(string versionContent)
    {
        var pythonFolderPath = _folders.PythonFolderPath;
        if (await _fileSystem.GetKindAsync(pythonFolderPath) != StorageItemKind.Folder)
        {
            _logger.LogDebug("Python reinstall required: pythonFolder does not exist at {Path}", pythonFolderPath);
            return false;
        }

        var installedVersionPath = _folders.InstalledVersionFilePath;
        if (await _fileSystem.GetKindAsync(installedVersionPath) != StorageItemKind.File)
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

        // Both sides are trimmed. A marker that gained or lost a trailing newline on the way to disk then
        // matches.
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

        // The marker describes what was installed, and the wheel can go while the marker still matches. Every
        // console needs the wheel.
        var findWheelResult = await FindWheelFileAsync(pythonFolderPath);
        if (findWheelResult.IsFailure)
        {
            _logger.LogDebug("Python reinstall required: no celbridge wheel in {Path}", pythonFolderPath);
            return false;
        }

        return true;
    }

    // Checks the published command, the tool environment's own interpreter, and the shared interpreter in the cache
    // folder. The environment runs on the shared interpreter. The marker describes what was installed, and the disk
    // shows what survived. The marker leaves out the cache folder, so the shared interpreter can go while the marker
    // still matches.
    private async Task<bool> IsToolInstalledAsync()
    {
        var requiredItems = new[]
        {
            _folders.CelbridgeToolCommandPath,
            _folders.CelbridgeToolInterpreterPath,
        };

        foreach (var path in requiredItems)
        {
            if (await _fileSystem.GetKindAsync(path) != StorageItemKind.File)
            {
                _logger.LogDebug("The celbridge tool is not installed: '{Path}' is missing", path);
                return false;
            }
        }

        var toolInterpreter = new ToolEnvironmentInterpreter(_fileSystem);
        if (!await toolInterpreter.ExistsAsync(_folders.CelbridgeToolConfigPath))
        {
            _logger.LogDebug("The celbridge tool is not installed: the interpreter named by '{Path}' is missing",
                _folders.CelbridgeToolConfigPath);
            return false;
        }

        return true;
    }

    // Returns the content of the installed folder's version marker. The content lists every input of the install:
    // the application version, the wheel's hash, the tool's pinned interpreter version and uv's pinned version. The
    // install reruns only when the content changes, so the content must list every input.
    private async Task<Result<string>> GetVersionContentAsync(string appVersion)
    {
        var findWheelResult = await FindWheelFileAsync(BundledAssetsFolderPath);
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

        var uvVersion = _uvDependency.Version;
        var pythonVersion = _pythonConfigService.DefaultPythonVersion;

        return Result<string>.Ok($"{appVersion}\n{wheelHash}\n{pythonVersion}\n{uvVersion}");
    }

    private async Task<Result> ReinstallAsync(string versionContent)
    {
        // The wheel and uv are found before anything is deleted, so an install that lacks either fails with the
        // working install still in place.
        var bundledAssetsFolderPath = BundledAssetsFolderPath;
        var findWheelResult = await FindWheelFileAsync(bundledAssetsFolderPath);
        if (findWheelResult.IsFailure)
        {
            return Result.Fail($"The bundled celbridge wheel is missing from '{bundledAssetsFolderPath}'")
                .WithErrors(findWheelResult);
        }

        var uvPath = _uvDependency.ExecutablePath;
        if (await _fileSystem.GetKindAsync(uvPath) != StorageItemKind.File)
        {
            return Result.Fail($"uv is missing from '{uvPath}'");
        }

        var pythonFolderPath = _folders.PythonFolderPath;
        if (await _fileSystem.GetKindAsync(pythonFolderPath) == StorageItemKind.Folder)
        {
            var deleteResult = await _fileSystem.DeleteFolderAsync(pythonFolderPath, recursive: true);
            if (deleteResult.IsFailure)
            {
                return Result.Fail(
                    $"Failed to delete the Python folder '{pythonFolderPath}'. " +
                    "A Python process may still hold its files. Close every Celbridge instance and try again.")
                    .WithErrors(deleteResult);
            }
        }

        var copyResult = await CopyBundledFolderAsync(bundledAssetsFolderPath, pythonFolderPath);
        if (copyResult.IsFailure)
        {
            return copyResult;
        }

        var toolResult = await InstallToolAsync();
        if (toolResult.IsFailure)
        {
            return toolResult;
        }

        // The marker is written last. An install that stops part way leaves no marker, and the next load installs
        // again.
        var writeResult = await _fileSystem.WriteAllTextAsync(_folders.InstalledVersionFilePath, versionContent);
        if (writeResult.IsFailure)
        {
            return Result.Fail("Failed to write the celbridge wheel's version marker")
                .WithErrors(writeResult);
        }

        return Result.Ok();
    }

    // Publishes the celbridge-py command from the wheel in the Python folder. The application needs one tool
    // environment, because the tool only runs the bootstrap shim. The shim's inner uv run provides the interpreter
    // and the packages the REPL imports.
    private async Task<Result> InstallToolAsync()
    {
        var findWheelResult = await FindWheelFileAsync(_folders.PythonFolderPath);
        if (findWheelResult.IsFailure)
        {
            return Result.Fail("Failed to find the celbridge wheel to install as a tool")
                .WithErrors(findWheelResult);
        }
        var wheelPath = findWheelResult.Value;

        var pythonVersion = _pythonConfigService.DefaultPythonVersion;

        _logger.LogInformation("Installing celbridge as a uv tool with Python {PythonVersion}", pythonVersion);

        var installTimer = Stopwatch.StartNew();

        // --no-config stops uv reading any configuration file. A user's uv.toml then cannot block the install or
        // change what it installs.
        var arguments = new List<string>
        {
            "tool",
            "install",
            "--force",
            "--no-config",
            "--python", pythonVersion,
            "--managed-python",
            wheelPath,
        };

        var variables = new Dictionary<string, string>
        {
            ["UV_TOOL_DIR"] = _folders.UvToolsFolderPath,
            ["UV_TOOL_BIN_DIR"] = _folders.UvToolBinFolderPath,
            ["UV_PYTHON_INSTALL_DIR"] = _folders.UvPythonInstallFolderPath,
            ["UV_CACHE_DIR"] = _folders.UvCacheFolderPath,
        };

        var installer = new UvToolInstaller(_uvRunner, _logger);
        var installResult = await installer.InstallAsync(arguments, variables, ToolInstallTimeout);
        if (installResult.IsFailure)
        {
            return installResult;
        }

        _logger.LogInformation("celbridge tool installed successfully in {DurationMs}ms", installTimer.ElapsedMilliseconds);
        _automationService.LogEvent(new PythonToolInstalledEvent(installTimer.ElapsedMilliseconds));

        return Result.Ok();
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

    // Copies a bundled assets folder, and everything in it, through the file system gateway.
    private async Task<Result> CopyBundledFolderAsync(string sourcePath, string destinationPath)
    {
        var createResult = await _fileSystem.CreateFolderAsync(destinationPath);
        if (createResult.IsFailure)
        {
            return Result.Fail($"Failed to create the folder '{destinationPath}'")
                .WithErrors(createResult);
        }

        var enumerateResult = await _fileSystem.EnumerateAsync(sourcePath, "*", recursive: true);
        if (enumerateResult.IsFailure)
        {
            return Result.Fail($"Failed to enumerate the bundled assets folder '{sourcePath}'")
                .WithErrors(enumerateResult);
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
                var createTargetResult = await _fileSystem.CreateFolderAsync(targetFolder);
                if (createTargetResult.IsFailure)
                {
                    return Result.Fail($"Failed to create the folder '{targetFolder}'")
                        .WithErrors(createTargetResult);
                }
            }

            var copyResult = await _fileSystem.CopyFileAsync(entry.FullPath, targetPath);
            if (copyResult.IsFailure)
            {
                return Result.Fail($"Failed to copy the bundled asset '{entry.FullPath}' to '{targetPath}'")
                    .WithErrors(copyResult);
            }
        }

        return Result.Ok();
    }
}
