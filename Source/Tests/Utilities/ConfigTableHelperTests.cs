using Celbridge.Utilities;
using Tomlyn;
using Tomlyn.Model;

namespace Celbridge.Tests.Utilities;

/// <summary>
/// Covers reading a config table the host does not model, as a TOML deserializer hands it over. A value of
/// the wrong shape reads as absent rather than failing the document, so these pin which shapes count and
/// which fall back.
/// </summary>
[TestFixture]
public class ConfigTableHelperTests
{
    private static readonly IReadOnlyDictionary<string, object?> Table = new Dictionary<string, object?>
    {
        ["text"] = "value",
        ["blank"] = "   ",
        ["number"] = 3.13,
        ["list"] = new object?[] { "one", "two" },
        ["mixed"] = new object?[] { "one", 2, "  ", "three" },
    };

    [Test]
    public void ReadText_AStringValue_IsReturned()
    {
        ConfigTableHelper.ReadText(Table, "text").Should().Be("value");
    }

    [TestCase("missing")]
    [TestCase("blank")]
    [TestCase("number")]
    [TestCase("list")]
    public void ReadText_AnythingButAStringWithContent_FallsBackToTheDefault(string key)
    {
        ConfigTableHelper.ReadText(Table, key).Should().BeEmpty();
        ConfigTableHelper.ReadText(Table, key, "fallback").Should().Be("fallback");
    }

    [Test]
    public void ReadTextList_AnArrayOfStrings_IsReturned()
    {
        ConfigTableHelper.ReadTextList(Table, "list").Should().Equal("one", "two");
    }

    [Test]
    public void ReadTextList_EntriesThatAreNotStringsOrAreBlank_AreSkipped()
    {
        ConfigTableHelper.ReadTextList(Table, "mixed").Should().Equal("one", "three");
    }

    [Test]
    public void ReadTextList_AStringValue_IsNotReadAsItsCharacters()
    {
        // A string is itself enumerable, so a scalar written where an array belongs must read as absent
        // rather than as one entry per character.
        ConfigTableHelper.ReadTextList(Table, "text").Should().BeEmpty();
    }

    [TestCase("missing")]
    [TestCase("number")]
    public void ReadTextList_AnythingButAnArray_ReadsAsEmpty(string key)
    {
        ConfigTableHelper.ReadTextList(Table, key).Should().BeEmpty();
    }

    [Test]
    public void ReadTable_ADictionary_IsReturned()
    {
        var nested = new Dictionary<string, object?> { ["key"] = "value" };

        var table = ConfigTableHelper.ReadTable(nested);

        table.Should().NotBeNull();
        ConfigTableHelper.ReadText(table!, "key").Should().Be("value");
    }

    [Test]
    public void ReadTable_ATomlynTable_IsReturned()
    {
        // Tomlyn models a table as IDictionary<string, object>, which is not the read-only interface the
        // signature takes, so this is the shape the console parser actually hands over.
        var document = TomlSerializer.Deserialize<TomlTable>("[section]\nkey = \"value\"\n");
        var section = document["section"];

        var table = ConfigTableHelper.ReadTable(section);

        table.Should().NotBeNull();
        ConfigTableHelper.ReadText(table!, "key").Should().Be("value");
    }

    [Test]
    public void ReadTable_AScalarOrAnArray_IsNotATable()
    {
        ConfigTableHelper.ReadTable("value").Should().BeNull();
        ConfigTableHelper.ReadTable(3.13).Should().BeNull();
        ConfigTableHelper.ReadTable(new object?[] { "one" }).Should().BeNull();
        ConfigTableHelper.ReadTable(null).Should().BeNull();
    }
}
