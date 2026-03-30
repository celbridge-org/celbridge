using Celbridge.Packages;

namespace Celbridge.Tests.Packages;

[TestFixture]
public class PackageLocalizationServiceTests
{
    private string _tempFolder = null!;
    private IPackageLocalizationService _service = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(PackageLocalizationServiceTests));
        Directory.CreateDirectory(_tempFolder);

        var logger = Substitute.For<ILogger<PackageLocalizationService>>();
        _service = new PackageLocalizationService(logger);
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
    public void LoadStrings_ExactLocale_ReturnsStrings()
    {
        var localizationFolder = Path.Combine(_tempFolder, PackageLocalizationService.LocalizationFolder);
        Directory.CreateDirectory(localizationFolder);
        File.WriteAllText(Path.Combine(localizationFolder, "fr.json"), """
            {
                "Greeting": "Bonjour",
                "Farewell": "Au revoir"
            }
            """);

        var result = _service.LoadStrings(_tempFolder, "fr");

        result.Should().HaveCount(2);
        result["Greeting"].Should().Be("Bonjour");
        result["Farewell"].Should().Be("Au revoir");
    }

    [Test]
    public void LoadStrings_MissingLocale_FallsBackToEnglish()
    {
        var localizationFolder = Path.Combine(_tempFolder, PackageLocalizationService.LocalizationFolder);
        Directory.CreateDirectory(localizationFolder);
        File.WriteAllText(Path.Combine(localizationFolder, "en.json"), """
            {
                "Hello": "Hello",
                "Bye": "Goodbye"
            }
            """);

        var result = _service.LoadStrings(_tempFolder, "ja");

        result.Should().HaveCount(2);
        result["Hello"].Should().Be("Hello");
        result["Bye"].Should().Be("Goodbye");
    }

    [Test]
    public void LoadStrings_NoLocaleFiles_ReturnsEmptyDictionary()
    {
        var localizationFolder = Path.Combine(_tempFolder, PackageLocalizationService.LocalizationFolder);
        Directory.CreateDirectory(localizationFolder);

        var result = _service.LoadStrings(_tempFolder, "de");

        result.Should().BeEmpty();
    }

    [Test]
    public void LoadStrings_NoLocalizationFolder_ReturnsEmptyDictionary()
    {
        var result = _service.LoadStrings(_tempFolder, "en");

        result.Should().BeEmpty();
    }

    [Test]
    public void LoadStrings_InvalidJson_ReturnsEmptyDictionary()
    {
        var localizationFolder = Path.Combine(_tempFolder, PackageLocalizationService.LocalizationFolder);
        Directory.CreateDirectory(localizationFolder);
        File.WriteAllText(Path.Combine(localizationFolder, "en.json"), "{ not valid json }");

        var result = _service.LoadStrings(_tempFolder, "en");

        result.Should().BeEmpty();
    }

    [Test]
    public void LoadStrings_EnglishRequested_DoesNotDoubleLoad()
    {
        var localizationFolder = Path.Combine(_tempFolder, PackageLocalizationService.LocalizationFolder);
        Directory.CreateDirectory(localizationFolder);
        File.WriteAllText(Path.Combine(localizationFolder, "en.json"), """
            {
                "Key": "Value"
            }
            """);

        var result = _service.LoadStrings(_tempFolder, "en");

        result.Should().HaveCount(1);
        result["Key"].Should().Be("Value");
    }

    [Test]
    public void LoadStrings_CommentsAndTrailingCommas_AreAllowed()
    {
        var localizationFolder = Path.Combine(_tempFolder, PackageLocalizationService.LocalizationFolder);
        Directory.CreateDirectory(localizationFolder);
        File.WriteAllText(Path.Combine(localizationFolder, "en.json"), """
            {
                // Comment
                "Key1": "Value1",
                "Key2": "Value2",
            }
            """);

        var result = _service.LoadStrings(_tempFolder, "en");

        result.Should().HaveCount(2);
        result["Key1"].Should().Be("Value1");
    }
}
