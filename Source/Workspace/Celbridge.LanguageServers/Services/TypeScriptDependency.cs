using Celbridge.Dependencies;
using Celbridge.FileSystem;
using Celbridge.LanguageServers.Helpers;
using Celbridge.Localization;
using Celbridge.Logging;
using Celbridge.Platform;

namespace Celbridge.LanguageServers.Services;

/// <summary>
/// The pinned release of TypeScript. Celbridge installs the release as a dependency. The TypeScript compiler is
/// also the language server for TypeScript and JavaScript.
/// </summary>
public sealed class TypeScriptDependency : IDependency
{
    private const string LanguageServersFolderName = "LanguageServers";
    private const string TypeScriptFolderName = "typescript";
    private const string PackageArchiveFolder = "package";
    private const string LibFolderName = "lib";
    private const string CompilerExecutableName = "tsc";
    private const string CompilerExecutableNameWindows = "tsc.exe";

    private readonly ILocalFileSystem _fileSystem;
    private readonly IAppEnvironment _appEnvironment;
    private readonly IDependencyDownloader _downloader;
    private readonly ILocalizerService _localizerService;
    private readonly ILogger<TypeScriptDependency> _logger;
    private readonly TypeScriptPinnedRelease _pin;

    public TypeScriptDependency(
        ILocalFileSystem fileSystem,
        IAppEnvironment appEnvironment,
        IDependencyDownloader downloader,
        ILocalizerService localizerService,
        ILogger<TypeScriptDependency> logger)
        : this(fileSystem, appEnvironment, downloader, localizerService, logger, TypeScriptPinnedRelease.Load())
    {
    }

    internal TypeScriptDependency(
        ILocalFileSystem fileSystem,
        IAppEnvironment appEnvironment,
        IDependencyDownloader downloader,
        ILocalizerService localizerService,
        ILogger<TypeScriptDependency> logger,
        TypeScriptPinnedRelease pin)
    {
        _fileSystem = fileSystem;
        _appEnvironment = appEnvironment;
        _downloader = downloader;
        _localizerService = localizerService;
        _logger = logger;
        _pin = pin;
    }

    public string Id => "typescript";

    public string Version => _pin.Version;

    public string Name => _localizerService.GetString("Dependency_TypeScript_Name");

    public string ProgressTitle => _localizerService.GetString("Dependency_TypeScript_ProgressTitle");

    /// <summary>
    /// The folder that contains the pinned version's npm package, whether or not it is installed yet. Each version
    /// has a folder named by the version.
    /// </summary>
    public string FolderPath => Path.Combine(VersionsFolderPath, Version);

    /// <summary>
    /// The pinned version's compiler executable, whether or not it is installed yet.
    /// </summary>
    public string ExecutablePath => Path.Combine(
        FolderPath,
        LibFolderName,
        OperatingSystem.IsWindows() ? CompilerExecutableNameWindows : CompilerExecutableName);

    private string VersionsFolderPath => Path.Combine(
        _appEnvironment.DependenciesFolderPath,
        LanguageServersFolderName,
        TypeScriptFolderName);

    public async Task<bool> IsInstalledAsync()
    {
        return await _fileSystem.GetKindAsync(ExecutablePath) == StorageItemKind.File;
    }

    public async Task<Result> InstallAsync()
    {
        var platform = TypeScriptPinnedRelease.GetRunningPlatform();
        if (platform is null)
        {
            return Result.Fail("TypeScript publishes no package for this platform");
        }

        var package = _pin.FindPackage(platform);
        if (package is null)
        {
            return Result.Fail($"The TypeScript pin lists no package for the platform '{platform}'");
        }

        // Another copy of Celbridge may have installed TypeScript since the check.
        if (await IsInstalledAsync())
        {
            return Result.Ok();
        }

        // A folder that lacks the executable is incomplete, and the download's rename needs the name free.
        if (await _fileSystem.GetKindAsync(FolderPath) == StorageItemKind.Folder)
        {
            var deleteResult = await _fileSystem.DeleteFolderAsync(FolderPath, recursive: true);
            if (deleteResult.IsFailure)
            {
                return Result.Fail($"Failed to delete the incomplete folder of TypeScript {Version}")
                    .WithErrors(deleteResult);
            }
        }

        var archiveHash = TypeScriptPinnedRelease.GetArchiveHash(package);
        var download = new DependencyDownload(_pin.GetArchiveUrl(package), archiveHash, FolderPath)
        {
            ArchiveFolder = PackageArchiveFolder
        };

        var downloadResult = await _downloader.DownloadAsync(download);
        if (downloadResult.IsFailure)
        {
            return Result.Fail($"Failed to download TypeScript {Version}")
                .WithErrors(downloadResult);
        }

        await DeleteOtherVersionsAsync();

        return Result.Ok();
    }

    // Deletes only the folders named by a version, so another copy of Celbridge keeps the temporary folder of a
    // download in progress. A folder that fails to delete is logged and stays in place.
    private async Task DeleteOtherVersionsAsync()
    {
        var enumerateResult = await _fileSystem.EnumerateAsync(VersionsFolderPath, "*", recursive: false);
        if (enumerateResult.IsFailure)
        {
            _logger.LogWarning(enumerateResult, $"Failed to list the TypeScript versions in '{VersionsFolderPath}'");
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
                _logger.LogWarning(deleteResult, $"Failed to delete the folder of TypeScript {folderName}");
                continue;
            }

            _logger.LogInformation("Deleted the folder of TypeScript {Version}", folderName);
        }
    }
}
