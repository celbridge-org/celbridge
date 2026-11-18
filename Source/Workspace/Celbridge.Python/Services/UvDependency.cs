using Celbridge.Dependencies;
using Celbridge.FileSystem;
using Celbridge.Localization;
using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.Python.Helpers;

namespace Celbridge.Python.Services;

/// <summary>
/// The pinned release of uv, which installs and runs Python for Celbridge. Each version downloads from its GitHub
/// release into a folder in the dependencies folder, named by the version. The folder contains uv's binaries alone.
/// The check looks for uv's executable in the folder. A machine downloads uv again after a release of Celbridge
/// changes the pin, or after the executable has gone.
/// </summary>
public sealed class UvDependency : IDependency
{
    private const string UvFolderName = "uv";
    private const string UvExecutableName = "uv";
    private const string UvExecutableNameWindows = "uv.exe";

    private readonly ILocalFileSystem _fileSystem;
    private readonly IAppEnvironment _appEnvironment;
    private readonly IDependencyDownloader _downloader;
    private readonly ILocalizerService _localizerService;
    private readonly ILogger<UvDependency> _logger;
    private readonly UvPinnedRelease _pin;

    public UvDependency(
        ILocalFileSystem fileSystem,
        IAppEnvironment appEnvironment,
        IDependencyDownloader downloader,
        ILocalizerService localizerService,
        ILogger<UvDependency> logger)
        : this(fileSystem, appEnvironment, downloader, localizerService, logger, UvPinnedRelease.Load())
    {
    }

    internal UvDependency(
        ILocalFileSystem fileSystem,
        IAppEnvironment appEnvironment,
        IDependencyDownloader downloader,
        ILocalizerService localizerService,
        ILogger<UvDependency> logger,
        UvPinnedRelease pin)
    {
        _fileSystem = fileSystem;
        _appEnvironment = appEnvironment;
        _downloader = downloader;
        _localizerService = localizerService;
        _logger = logger;
        _pin = pin;
    }

    public string Id => "uv";

    public string Version => _pin.Version;

    public string Name => _localizerService.GetString("Dependency_Uv_Name");

    public string ProgressTitle => _localizerService.GetString("Dependency_Uv_ProgressTitle");

    /// <summary>
    /// The folder that contains the pinned version's uv and uvx, whether or not it is installed yet.
    /// </summary>
    public string FolderPath => Path.Combine(UvVersionsFolderPath, Version);

    /// <summary>
    /// The pinned version's uv executable, whether or not it is installed yet.
    /// </summary>
    public string ExecutablePath => Path.Combine(
        FolderPath,
        OperatingSystem.IsWindows() ? UvExecutableNameWindows : UvExecutableName);

    private string UvVersionsFolderPath => Path.Combine(_appEnvironment.DependenciesFolderPath, UvFolderName);

    public async Task<bool> IsInstalledAsync()
    {
        return await _fileSystem.GetKindAsync(ExecutablePath) == StorageItemKind.File;
    }

    public async Task<Result> InstallAsync()
    {
        var platform = UvPinnedRelease.GetRunningPlatform();
        if (platform is null)
        {
            return Result.Fail("uv publishes no release for this platform");
        }

        var archive = _pin.FindArchive(platform);
        if (archive is null)
        {
            return Result.Fail($"The uv pin lists no archive for the platform '{platform}'");
        }

        // Another copy of Celbridge may have installed uv since the check.
        if (await IsInstalledAsync())
        {
            return Result.Ok();
        }

        // A folder that lacks uv's executable is incomplete, and the download's rename needs the name free.
        if (await _fileSystem.GetKindAsync(FolderPath) == StorageItemKind.Folder)
        {
            var deleteResult = await _fileSystem.DeleteFolderAsync(FolderPath, recursive: true);
            if (deleteResult.IsFailure)
            {
                return Result.Fail($"Failed to delete the incomplete folder of uv {Version}")
                    .WithErrors(deleteResult);
            }
        }

        var download = new DependencyDownload(_pin.GetArchiveUrl(archive), archive.Sha256, FolderPath)
        {
            ArchiveFolder = archive.Folder
        };

        var downloadResult = await _downloader.DownloadAsync(download);
        if (downloadResult.IsFailure)
        {
            return Result.Fail($"Failed to download uv {Version}")
                .WithErrors(downloadResult);
        }

        await DeleteOtherVersionsAsync();

        return Result.Ok();
    }

    // Deletes only the folders named by a version, so another copy of Celbridge keeps the temporary folder of a
    // download in progress. A folder that fails to delete is logged and stays in place.
    private async Task DeleteOtherVersionsAsync()
    {
        var enumerateResult = await _fileSystem.EnumerateAsync(UvVersionsFolderPath, "*", recursive: false);
        if (enumerateResult.IsFailure)
        {
            _logger.LogWarning(enumerateResult, $"Failed to list the uv versions in '{UvVersionsFolderPath}'");
            return;
        }

        foreach (var entry in enumerateResult.Value)
        {
            var folderName = Path.GetFileName(entry.FullPath);
            if (!entry.IsFolder
                || folderName == Version
                || !SemanticVersion.TryParse(folderName, out _))
            {
                continue;
            }

            var deleteResult = await _fileSystem.DeleteFolderAsync(entry.FullPath, recursive: true);
            if (deleteResult.IsFailure)
            {
                _logger.LogWarning(deleteResult, $"Failed to delete the folder of uv {folderName}");
                continue;
            }

            _logger.LogInformation("Deleted the folder of uv {Version}", folderName);
        }
    }
}
