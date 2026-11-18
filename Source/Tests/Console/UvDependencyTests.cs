using Celbridge.Dependencies;
using Celbridge.FileSystem.Services;
using Celbridge.Localization;
using Celbridge.Platform;
using Celbridge.Python.Helpers;
using Celbridge.Python.Services;
using Celbridge.Tests.Helpers;

namespace Celbridge.Tests.Console;

/// <summary>
/// Covers uv as a dependency. The tests cover the check for the pinned version's folder and the download for the
/// running platform. The tests also cover the deletion of other versions' folders after a new version installs, and
/// the assembly's embedded pin.
/// </summary>
[TestFixture]
public class UvDependencyTests
{
    private const string PinnedVersion = "0.10.11";

    private string _dependenciesFolderPath = null!;
    private string _uvFolderPath = null!;
    private IDependencyDownloader _downloader = null!;

    [SetUp]
    public void Setup()
    {
        _dependenciesFolderPath = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(UvDependencyTests), Guid.NewGuid().ToString("N"));
        _uvFolderPath = Path.Combine(_dependenciesFolderPath, "uv");
        Directory.CreateDirectory(_uvFolderPath);

        // A download that succeeds creates the version's folder. The real downloader does the same.
        _downloader = Substitute.For<IDependencyDownloader>();
        _downloader.DownloadAsync(Arg.Any<DependencyDownload>()).Returns(callInfo =>
        {
            var download = (DependencyDownload)callInfo[0];
            Directory.CreateDirectory(download.FolderPath);

            return Result.Ok();
        });
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dependenciesFolderPath))
        {
            Directory.Delete(_dependenciesFolderPath, recursive: true);
        }
    }

    [Test]
    public async Task IsInstalled_ThePinnedVersionsUvPresent_ReturnsTrue()
    {
        CreateVersionFolder(PinnedVersion, UvExecutableName);
        var dependency = CreateDependency();

        var isInstalled = await dependency.IsInstalledAsync();

        isInstalled.Should().BeTrue();
    }

    [Test]
    public async Task IsInstalled_AVersionFolderWithoutUv_ReturnsFalse()
    {
        CreateVersionFolder(PinnedVersion, "uvx");
        var dependency = CreateDependency();

        var isInstalled = await dependency.IsInstalledAsync();

        isInstalled.Should().BeFalse();
    }

    [Test]
    public async Task IsInstalled_OnlyAnotherVersionsFolderPresent_ReturnsFalse()
    {
        Directory.CreateDirectory(Path.Combine(_uvFolderPath, "0.9.0"));
        var dependency = CreateDependency();

        var isInstalled = await dependency.IsInstalledAsync();

        isInstalled.Should().BeFalse();
    }

    [Test]
    public async Task Install_DownloadsTheRunningPlatformsArchiveIntoTheVersionsFolder()
    {
        var platform = UvPinnedRelease.GetRunningPlatform();
        Assume.That(platform, Is.Not.Null, "uv publishes no release for this platform");
        var dependency = CreateDependency();

        var result = await dependency.InstallAsync();

        result.IsSuccess.Should().BeTrue(result.MessageChain);
        var expectedDownload = new DependencyDownload(
            $"https://example.com/releases/{PinnedVersion}/uv-{platform}.tar.gz",
            "abc123",
            Path.Combine(_uvFolderPath, PinnedVersion))
        {
            ArchiveFolder = $"uv-{platform}"
        };
        await _downloader.Received(1).DownloadAsync(expectedDownload);
    }

    [Test]
    public async Task Install_AVersionFolderWithoutUv_IsReplacedByTheDownload()
    {
        Assume.That(UvPinnedRelease.GetRunningPlatform(), Is.Not.Null, "uv publishes no release for this platform");
        var folderPath = CreateVersionFolder(PinnedVersion, "uvx");
        var dependency = CreateDependency();

        var result = await dependency.InstallAsync();

        result.IsSuccess.Should().BeTrue(result.MessageChain);
        await _downloader.Received(1).DownloadAsync(Arg.Any<DependencyDownload>());
        Directory.Exists(folderPath).Should().BeTrue();
        File.Exists(Path.Combine(folderPath, "uvx")).Should().BeFalse("the incomplete folder was deleted first");
    }

    [Test]
    public async Task Install_UvThatAnotherCopyInstalledFirst_DownloadsNothing()
    {
        CreateVersionFolder(PinnedVersion, UvExecutableName);
        var dependency = CreateDependency();

        var result = await dependency.InstallAsync();

        result.IsSuccess.Should().BeTrue(result.MessageChain);
        await _downloader.DidNotReceive().DownloadAsync(Arg.Any<DependencyDownload>());
    }

    [Test]
    public async Task Install_ANewVersion_DeletesTheOtherVersionsFolders()
    {
        Assume.That(UvPinnedRelease.GetRunningPlatform(), Is.Not.Null, "uv publishes no release for this platform");
        Directory.CreateDirectory(Path.Combine(_uvFolderPath, "0.9.0"));
        Directory.CreateDirectory(Path.Combine(_uvFolderPath, ".download-0123456789abcdef"));
        Directory.CreateDirectory(Path.Combine(_uvFolderPath, "notes"));
        var dependency = CreateDependency();

        var result = await dependency.InstallAsync();

        result.IsSuccess.Should().BeTrue(result.MessageChain);
        var folderNames = Directory.EnumerateDirectories(_uvFolderPath)
            .Select(path => Path.GetFileName(path));
        folderNames.Should().BeEquivalentTo(PinnedVersion, ".download-0123456789abcdef", "notes");
    }

    [Test]
    public async Task Install_AFailedDownload_KeepsTheOtherVersionsFolders()
    {
        Assume.That(UvPinnedRelease.GetRunningPlatform(), Is.Not.Null, "uv publishes no release for this platform");
        Directory.CreateDirectory(Path.Combine(_uvFolderPath, "0.9.0"));
        _downloader.DownloadAsync(Arg.Any<DependencyDownload>()).Returns(Result.Fail("The network is down"));
        var dependency = CreateDependency();

        var result = await dependency.InstallAsync();

        result.IsFailure.Should().BeTrue();
        Directory.Exists(Path.Combine(_uvFolderPath, "0.9.0")).Should().BeTrue();
    }

    [Test]
    public void Load_ReadsTheEmbeddedPin_WithAnArchiveForTheRunningPlatform()
    {
        var pin = UvPinnedRelease.Load();

        SemanticVersion.TryParse(pin.Version, out _).Should().BeTrue();
        var platform = UvPinnedRelease.GetRunningPlatform();
        if (platform is not null)
        {
            var archive = pin.FindArchive(platform);
            archive.Should().NotBeNull();
            archive!.Sha256.Should().MatchRegex("^[0-9a-f]{64}$");
            pin.GetArchiveUrl(archive).Should().Be($"https://github.com/astral-sh/uv/releases/download/{pin.Version}/{archive.Name}");
        }
    }

    [Test]
    public void Parse_AnArchiveWithoutAHash_Throws()
    {
        var pinText = """
            <UvPinnedRelease Version="0.10.11" ReleasesUrl="https://example.com/releases">
              <Archive Platform="aarch64-apple-darwin" Name="uv-aarch64-apple-darwin.tar.gz" />
            </UvPinnedRelease>
            """;

        var parse = () => UvPinnedRelease.Parse(pinText);

        parse.Should().Throw<FormatException>().WithMessage("*Sha256*");
    }

    private static string UvExecutableName => OperatingSystem.IsWindows() ? "uv.exe" : "uv";

    private string CreateVersionFolder(string version, string fileName)
    {
        var folderPath = Path.Combine(_uvFolderPath, version);
        Directory.CreateDirectory(folderPath);
        File.WriteAllText(Path.Combine(folderPath, fileName), "a binary");

        return folderPath;
    }

    // The pin lists the running platform's archive as a .tar.gz with its binaries in a folder, the form that uv
    // publishes for macOS and Linux.
    private UvDependency CreateDependency()
    {
        var archives = new List<UvArchive>();
        var platform = UvPinnedRelease.GetRunningPlatform();
        if (platform is not null)
        {
            archives.Add(new UvArchive(platform, $"uv-{platform}.tar.gz", "abc123", $"uv-{platform}"));
        }
        var pin = new UvPinnedRelease(PinnedVersion, "https://example.com/releases", archives);

        var appEnvironment = Substitute.For<IAppEnvironment>();
        appEnvironment.DependenciesFolderPath.Returns(_dependenciesFolderPath);

        return new UvDependency(
            new LocalFileSystem(new NullLogger<LocalFileSystem>()),
            appEnvironment,
            _downloader,
            Substitute.For<ILocalizerService>(),
            new NullLogger<UvDependency>(),
            pin);
    }
}
