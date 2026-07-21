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
          ".json": { "language": "json", "categories": ["Text", "Data"], "display-name": "JSON" },
          ".png": { "categories": ["Image"], "display-name": "PNG Image" },
          ".bogus": { "categories": ["NotACategory"] }
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
    public async Task GetCategories_MultiCategoryExtension_ReturnsAllCategoriesInOrder()
    {
        var catalog = await LoadCatalogAsync(CatalogJson);

        catalog.GetCategories(".json").Should().Equal(FileTypeCategory.Text, FileTypeCategory.Data);
    }

    [Test]
    public async Task GetCategories_IsCaseInsensitive()
    {
        var catalog = await LoadCatalogAsync(CatalogJson);

        catalog.GetCategories(".PNG").Should().Equal(FileTypeCategory.Image);
    }

    [Test]
    public async Task GetCategories_ExtensionWithOnlyALanguage_ReturnsEmpty()
    {
        // A code extension the catalog assigns no category defaults to Text at the call site, not here.
        var catalog = await LoadCatalogAsync(CatalogJson);

        catalog.GetCategories(".cs").Should().BeEmpty();
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
    public async Task Load_UnknownCategoryName_IsSkippedWithoutFailingTheEntry()
    {
        var catalog = await LoadCatalogAsync(CatalogJson);

        catalog.GetCategories(".bogus").Should().BeEmpty();
    }

    [Test]
    public async Task Load_MalformedCatalog_LeavesEveryExtensionUncatalogued()
    {
        // A broken catalog must not stop the application; the code editor's package reports the
        // resulting load failure instead.
        var catalog = await LoadCatalogAsync("{ not json");

        catalog.GetCategories(".json").Should().BeEmpty();
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
        catalog.GetCategories(".json").Should().Equal(FileTypeCategory.Text, FileTypeCategory.Data);
        catalog.GetCategories(".png").Should().Equal(FileTypeCategory.Image);
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
