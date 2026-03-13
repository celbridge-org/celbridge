using Celbridge.Extensions;

namespace Celbridge.Tests.Extensions;

[TestFixture]
public class LocalizationHelperTests
{
    private string _tempFolder = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(LocalizationHelperTests));
        Directory.CreateDirectory(_tempFolder);
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
        var locDir = Path.Combine(_tempFolder, LocalizationHelper.LocalizationFolder);
        Directory.CreateDirectory(locDir);
        File.WriteAllText(Path.Combine(locDir, "fr.json"), """
            {
                "Greeting": "Bonjour",
                "Farewell": "Au revoir"
            }
            """);

        var result = LocalizationHelper.LoadStrings(_tempFolder, "fr");

        result.Should().HaveCount(2);
        result["Greeting"].Should().Be("Bonjour");
        result["Farewell"].Should().Be("Au revoir");
    }

    [Test]
    public void LoadStrings_MissingLocale_FallsBackToEnglish()
    {
        var locDir = Path.Combine(_tempFolder, LocalizationHelper.LocalizationFolder);
        Directory.CreateDirectory(locDir);
        File.WriteAllText(Path.Combine(locDir, "en.json"), """
            {
                "Hello": "Hello",
                "Bye": "Goodbye"
            }
            """);

        var result = LocalizationHelper.LoadStrings(_tempFolder, "ja");

        result.Should().HaveCount(2);
        result["Hello"].Should().Be("Hello");
        result["Bye"].Should().Be("Goodbye");
    }

    [Test]
    public void LoadStrings_NoLocaleFiles_ReturnsEmptyDictionary()
    {
        var locDir = Path.Combine(_tempFolder, LocalizationHelper.LocalizationFolder);
        Directory.CreateDirectory(locDir);

        var result = LocalizationHelper.LoadStrings(_tempFolder, "de");

        result.Should().BeEmpty();
    }

    [Test]
    public void LoadStrings_NoLocalizationDirectory_ReturnsEmptyDictionary()
    {
        var result = LocalizationHelper.LoadStrings(_tempFolder, "en");

        result.Should().BeEmpty();
    }

    [Test]
    public void LoadStrings_InvalidJson_ReturnsEmptyDictionary()
    {
        var locDir = Path.Combine(_tempFolder, LocalizationHelper.LocalizationFolder);
        Directory.CreateDirectory(locDir);
        File.WriteAllText(Path.Combine(locDir, "en.json"), "{ not valid json }");

        var result = LocalizationHelper.LoadStrings(_tempFolder, "en");

        result.Should().BeEmpty();
    }

    [Test]
    public void LoadStrings_EnglishRequested_DoesNotDoubleLoad()
    {
        var locDir = Path.Combine(_tempFolder, LocalizationHelper.LocalizationFolder);
        Directory.CreateDirectory(locDir);
        File.WriteAllText(Path.Combine(locDir, "en.json"), """
            {
                "Key": "Value"
            }
            """);

        var result = LocalizationHelper.LoadStrings(_tempFolder, "en");

        result.Should().HaveCount(1);
        result["Key"].Should().Be("Value");
    }

    [Test]
    public void LoadStrings_CommentsAndTrailingCommas_AreAllowed()
    {
        var locDir = Path.Combine(_tempFolder, LocalizationHelper.LocalizationFolder);
        Directory.CreateDirectory(locDir);
        File.WriteAllText(Path.Combine(locDir, "en.json"), """
            {
                // Comment
                "Key1": "Value1",
                "Key2": "Value2",
            }
            """);

        var result = LocalizationHelper.LoadStrings(_tempFolder, "en");

        result.Should().HaveCount(2);
        result["Key1"].Should().Be("Value1");
    }
}
