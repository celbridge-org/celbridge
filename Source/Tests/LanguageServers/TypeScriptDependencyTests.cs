using Celbridge.Dependencies;
using Celbridge.FileSystem.Services;
using Celbridge.LanguageServers.Helpers;
using Celbridge.LanguageServers.Services;
using Celbridge.Localization;
using Celbridge.Platform;
using Celbridge.Tests.Helpers;

namespace Celbridge.Tests.LanguageServers;

/// <summary>
/// Covers TypeScript as a dependency, and the reader of its pin.
/// </summary>
[TestFixture]
public class TypeScriptDependencyTests
{
    private const string PinnedVersion = "7.0.2";

    // The SHA-512 hash of an empty archive, in the registry's form and as hex.
    private const string EmptyIntegrity = "sha512-z4PhNX7vuL3xVChQ1m2AB9Yg5AULVxXcg/SpIdNs6c5H0NE8XYXysP+DGNKHfuwvY7kxvUdBeoGlODJ6+SfaPg==";
    private const string EmptyHashHex = "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e";

    private string _dependenciesFolderPath = null!;
    private string _typeScriptFolderPath = null!;
    private IDependencyDownloader _downloader = null!;

    [SetUp]
    public void Setup()
    {
        _dependenciesFolderPath = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(TypeScriptDependencyTests), Guid.NewGuid().ToString("N"));
        _typeScriptFolderPath = Path.Combine(_dependenciesFolderPath, "LanguageServers", "typescript");
        Directory.CreateDirectory(_typeScriptFolderPath);

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
    public async Task IsInstalled_ThePinnedVersionsCompilerPresent_ReturnsTrue()
    {
        CreateVersionFolder(PinnedVersion, CompilerExecutableName);
        var dependency = CreateDependency();

        var isInstalled = await dependency.IsInstalledAsync();

        isInstalled.Should().BeTrue();
    }

    [Test]
    public async Task IsInstalled_AVersionFolderWithoutTheCompiler_ReturnsFalse()
    {
        CreateVersionFolder(PinnedVersion, "lib.d.ts");
        var dependency = CreateDependency();

        var isInstalled = await dependency.IsInstalledAsync();

        isInstalled.Should().BeFalse();
    }

    [Test]
    public async Task Install_DownloadsTheRunningPlatformsPackageIntoTheVersionsFolder()
    {
        var platform = TypeScriptPinnedRelease.GetRunningPlatform();
        Assume.That(platform, Is.Not.Null, "TypeScript publishes no package for this platform");
        var dependency = CreateDependency();

        var result = await dependency.InstallAsync();

        result.IsSuccess.Should().BeTrue(result.MessageChain);
        var expectedDownload = new DependencyDownload(
            $"https://example.com/registry/@typescript/typescript-{platform}/-/typescript-{platform}-{PinnedVersion}.tgz",
            new DependencyHash(DependencyHashAlgorithm.Sha512, EmptyHashHex),
            Path.Combine(_typeScriptFolderPath, PinnedVersion))
        {
            ArchiveFolder = "package"
        };
        await _downloader.Received(1).DownloadAsync(expectedDownload);
    }

    [Test]
    public async Task Install_AVersionFolderWithoutTheCompiler_IsReplacedByTheDownload()
    {
        Assume.That(TypeScriptPinnedRelease.GetRunningPlatform(), Is.Not.Null, "TypeScript publishes no package for this platform");
        var folderPath = CreateVersionFolder(PinnedVersion, "lib.d.ts");
        var dependency = CreateDependency();

        var result = await dependency.InstallAsync();

        result.IsSuccess.Should().BeTrue(result.MessageChain);
        await _downloader.Received(1).DownloadAsync(Arg.Any<DependencyDownload>());
        Directory.Exists(Path.Combine(folderPath, "lib")).Should().BeFalse("the incomplete folder was deleted first");
    }

    [Test]
    public async Task Install_ANewVersion_DeletesTheOtherVersionsFolders()
    {
        Assume.That(TypeScriptPinnedRelease.GetRunningPlatform(), Is.Not.Null, "TypeScript publishes no package for this platform");
        Directory.CreateDirectory(Path.Combine(_typeScriptFolderPath, "7.0.1"));
        Directory.CreateDirectory(Path.Combine(_typeScriptFolderPath, ".download-0123456789abcdef"));
        var dependency = CreateDependency();

        var result = await dependency.InstallAsync();

        result.IsSuccess.Should().BeTrue(result.MessageChain);
        var folderNames = Directory.EnumerateDirectories(_typeScriptFolderPath)
            .Select(path => Path.GetFileName(path));
        folderNames.Should().BeEquivalentTo(PinnedVersion, ".download-0123456789abcdef");
    }

    [Test]
    public async Task Install_AFailedDownload_KeepsTheOtherVersionsFolders()
    {
        Assume.That(TypeScriptPinnedRelease.GetRunningPlatform(), Is.Not.Null, "TypeScript publishes no package for this platform");
        Directory.CreateDirectory(Path.Combine(_typeScriptFolderPath, "7.0.1"));
        _downloader.DownloadAsync(Arg.Any<DependencyDownload>()).Returns(Result.Fail("The network is down"));
        var dependency = CreateDependency();

        var result = await dependency.InstallAsync();

        result.IsFailure.Should().BeTrue();
        Directory.Exists(Path.Combine(_typeScriptFolderPath, "7.0.1")).Should().BeTrue();
    }

    [Test]
    public void Load_ReadsTheEmbeddedPin_WithAPackageForTheRunningPlatform()
    {
        var pin = TypeScriptPinnedRelease.Load();

        SemanticVersion.TryParse(pin.Version, out _).Should().BeTrue();
        var platform = TypeScriptPinnedRelease.GetRunningPlatform();
        if (platform is not null)
        {
            var package = pin.FindPackage(platform);
            package.Should().NotBeNull();
            pin.GetArchiveUrl(package!).Should().Be(
                $"https://registry.npmjs.org/@typescript/typescript-{platform}/-/typescript-{platform}-{pin.Version}.tgz");
        }
    }

    [Test]
    public void GetArchiveHash_ConvertsTheRegistrysIntegrityValueToHex()
    {
        var package = new TypeScriptPackage("linux-x64", "@typescript/typescript-linux-x64", EmptyIntegrity);

        var hash = TypeScriptPinnedRelease.GetArchiveHash(package);

        hash.Should().Be(new DependencyHash(DependencyHashAlgorithm.Sha512, EmptyHashHex));
    }

    [Test]
    public void Parse_APackageWithAnotherHashAlgorithm_Throws()
    {
        var pinText = """
            <TypeScriptPinnedRelease Version="7.0.2" RegistryUrl="https://example.com/registry">
              <Package Platform="linux-x64" Name="@typescript/typescript-linux-x64" Integrity="sha1-2jmj7l5rSw0yVb/vlWAYkK/YBwk=" />
            </TypeScriptPinnedRelease>
            """;

        var parse = () => TypeScriptPinnedRelease.Parse(pinText);

        parse.Should().Throw<FormatException>().WithMessage("*SHA-512*");
    }

    private static string CompilerExecutableName => OperatingSystem.IsWindows() ? "tsc.exe" : "tsc";

    // The npm package keeps the compiler and its library files in the lib folder.
    private string CreateVersionFolder(string version, string fileName)
    {
        var folderPath = Path.Combine(_typeScriptFolderPath, version);
        var libFolderPath = Path.Combine(folderPath, "lib");
        Directory.CreateDirectory(libFolderPath);
        File.WriteAllText(Path.Combine(libFolderPath, fileName), "a binary");

        return folderPath;
    }

    private TypeScriptDependency CreateDependency()
    {
        var packages = new List<TypeScriptPackage>();
        var platform = TypeScriptPinnedRelease.GetRunningPlatform();
        if (platform is not null)
        {
            packages.Add(new TypeScriptPackage(platform, $"@typescript/typescript-{platform}", EmptyIntegrity));
        }
        var pin = new TypeScriptPinnedRelease(PinnedVersion, "https://example.com/registry", packages);

        var appEnvironment = Substitute.For<IAppEnvironment>();
        appEnvironment.DependenciesFolderPath.Returns(_dependenciesFolderPath);

        return new TypeScriptDependency(
            new LocalFileSystem(new NullLogger<LocalFileSystem>()),
            appEnvironment,
            _downloader,
            Substitute.For<ILocalizerService>(),
            new NullLogger<TypeScriptDependency>(),
            pin);
    }
}
