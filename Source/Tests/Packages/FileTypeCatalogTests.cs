using Celbridge.FileSystem;
using Celbridge.Packages;
using Celbridge.Platform;
using Celbridge.Tests.FileSystem;

namespace Celbridge.Tests.Packages;

[TestFixture]
public class FileTypeCatalogTests
{
    private const string CatalogJson = """
        {
          ".cs": { "language": "csharp" },
          ".json": { "language": "json", "display-name": "JSON" },
          ".png": { "display-name": "PNG Image", "icon": "image", "icon-color": "#40A0FF" }
        }
        """;

    private string _tempFolder = null!;
    private string _catalogPath = null!;
    private ILocalFileSystem _fileSystem = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(FileTypeCatalogTests));
        Directory.CreateDirectory(Path.Combine(_tempFolder, "celbridge-client"));
        _catalogPath = Path.Combine(_tempFolder, "celbridge-client", "file-types.json");
        _fileSystem = TestFileSystem.CreateLocal();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, true);
        }
    }

    [Test]
    public async Task GetLanguage_AndLanguageExtensions_CoverOnlyEntriesWithALanguage()
    {
        var catalog = await LoadCatalogAsync(CatalogJson);

        catalog.GetLanguage(".cs").Should().Be("csharp");
        catalog.GetLanguage(".png").Should().BeEmpty();
        catalog.LanguageExtensions.Should().BeEquivalentTo([".cs", ".json"]);
    }

    [Test]
    public async Task GetDisplayName_ReturnsCatalogNameOrEmpty()
    {
        var catalog = await LoadCatalogAsync(CatalogJson);

        catalog.GetDisplayName(".png").Should().Be("PNG Image");
        catalog.GetDisplayName(".cs").Should().BeEmpty();
        catalog.GetDisplayName(".unknown").Should().BeEmpty();
    }

    [Test]
    public async Task GetIcon_AndIconExtensions_CoverOnlyEntriesWithAnIcon()
    {
        var catalog = await LoadCatalogAsync(CatalogJson);

        catalog.GetIcon(".png").Should().Be(new FileTypeIcon("image", "#40A0FF"));
        catalog.GetIcon(".cs").Should().BeNull();
        catalog.IconExtensions.Should().BeEquivalentTo([".png"]);
    }

    [Test]
    public async Task GetIcon_ReadsAnOptionalScale_DefaultingToOne()
    {
        var json = """
            {
              ".big": { "icon": "nf-seti-npm", "icon-color": "#CB3837", "icon-scale": 1.35 },
              ".plain": { "icon": "nf-seti-json", "icon-color": "#CBCB41" }
            }
            """;

        var catalog = await LoadCatalogAsync(json);

        catalog.GetIcon(".big")!.Scale.Should().Be(1.35);
        catalog.GetIcon(".plain")!.Scale.Should().Be(1.0);
    }

    [Test]
    public async Task Load_MalformedCatalog_LeavesEveryExtensionUncatalogued()
    {
        // A broken catalog must not stop the application; the code editor's package reports the
        // resulting load failure instead.
        var catalog = await LoadCatalogAsync("{ not json");

        catalog.GetDisplayName(".json").Should().BeEmpty();
        catalog.LanguageExtensions.Should().BeEmpty();
    }

    [Test]
    public async Task Load_MissingCatalogFile_LeavesEveryExtensionUncatalogued()
    {
        var catalog = CreateCatalog();
        await catalog.LoadAsync();

        catalog.LanguageExtensions.Should().BeEmpty();
    }

    [Test]
    public async Task Load_ShippedCatalog_ClassifiesKnownTypesAndCarriesTheCodeExtensions()
    {
        // The bundled catalog is the code editor's only source of extensions, so a missing or renamed
        // asset would silently leave it claiming nothing.
        var appEnvironment = Substitute.For<IAppEnvironment>();
        appEnvironment.SharedWebAssetsFolderPath.Returns(
            Path.Combine(AppContext.BaseDirectory, "Celbridge.WebHost", "Web"));

        var catalog = new FileTypeCatalog(
            Substitute.For<ILogger<FileTypeCatalog>>(),
            _fileSystem,
            appEnvironment);
        await catalog.LoadAsync();

        catalog.LanguageExtensions.Count.Should().BeGreaterThan(100);
        catalog.GetLanguage(".cs").Should().Be("csharp");
        catalog.GetDisplayName(".png").Should().Be("PNG Image");
    }

    private async Task<IFileTypeCatalog> LoadCatalogAsync(string json)
    {
        File.WriteAllText(_catalogPath, json);

        var catalog = CreateCatalog();
        await catalog.LoadAsync();

        return catalog;
    }

    private IFileTypeCatalog CreateCatalog()
    {
        var appEnvironment = Substitute.For<IAppEnvironment>();
        appEnvironment.SharedWebAssetsFolderPath.Returns(_tempFolder);

        return new FileTypeCatalog(
            Substitute.For<ILogger<FileTypeCatalog>>(),
            _fileSystem,
            appEnvironment);
    }
}
