using Celbridge.Dependencies;
using Celbridge.FileSystem;
using Celbridge.LanguageServers.Helpers;
using Celbridge.Localization;
using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.Utilities;

namespace Celbridge.LanguageServers.Services;

/// <summary>
/// The pinned release of TypeScript. Celbridge installs the release as a dependency. The TypeScript compiler is
/// also the language server for TypeScript and JavaScript.
/// </summary>
public sealed class TypeScriptDependency : IDependency
{
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

    private string VersionsFolderPath => Path.Combine(_appEnvironment.LanguageServersFolderPath, TypeScriptFolderName);

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

        var archiveHash = TypeScriptPinnedRelease.GetArchiveHash(package);
        var download = new DependencyDownload(_pin.GetArchiveUrl(package), archiveHash, FolderPath)
        {
            ArchiveFolder = PackageArchiveFolder
        };

        var installer = new VersionedFolderInstaller(_fileSystem, _downloader, _logger);
        var installResult = await installer.InstallAsync(download, ExecutablePath);
        if (installResult.IsFailure)
        {
            return Result.Fail($"Failed to install TypeScript {Version}")
                .WithErrors(installResult);
        }

        return Result.Ok();
    }
}
