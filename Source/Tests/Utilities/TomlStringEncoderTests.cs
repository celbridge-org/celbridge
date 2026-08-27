using Tomlyn;
using Tomlyn.Model;

namespace Celbridge.Tests.Utilities;

[TestFixture]
public class TomlStringEncoderTests
{
    [Test]
    public void EncodeBasicString_PlainValue_IsQuotedUnchanged()
    {
        TomlStringEncoder.EncodeBasicString("https://example.com")
            .Should().Be("\"https://example.com\"");
    }

    [Test]
    public void EncodeBasicString_EscapesQuotesAndBackslashes()
    {
        TomlStringEncoder.EncodeBasicString("a\"b\\c")
            .Should().Be("\"a\\\"b\\\\c\"");
    }

    // A named escape rather than \uXXXX, which is the point of writing these files by hand: the escape
    // reads as itself in a diff.
    [TestCase("\t", "\"\\t\"")]
    [TestCase("\n", "\"\\n\"")]
    [TestCase("\r", "\"\\r\"")]
    [TestCase("\b", "\"\\b\"")]
    [TestCase("\f", "\"\\f\"")]
    public void EncodeBasicString_UsesTheNamedEscapeWhenThereIsOne(string value, string expected)
    {
        TomlStringEncoder.EncodeBasicString(value).Should().Be(expected);
    }

    [TestCase("\u0001", "\"\\u0001\"")]
    [TestCase("\u001f", "\"\\u001F\"")]
    [TestCase("\u007f", "\"\\u007F\"")]
    public void EncodeBasicString_EscapesTheRemainingControlCharactersAsUnicode(string value, string expected)
    {
        TomlStringEncoder.EncodeBasicString(value).Should().Be(expected);
    }

    [TestCase("plain")]
    [TestCase("")]
    [TestCase("has \"quotes\" and a \\ backslash")]
    [TestCase("tab\there")]
    [TestCase("newline\nhere")]
    [TestCase("carriage\rreturn")]
    [TestCase("control\u0001character")]
    [TestCase("delete\u007fcharacter")]
    [TestCase("trailing backslash \\")]
    public void EncodeBasicString_RoundTripsThroughTheParser(string value)
    {
        ReadValue(TomlStringEncoder.EncodeBasicString(value)).Should().Be(value);
    }

    [Test]
    public void EncodeMultilineBasicString_HoldsNewlinesAndTabsVerbatim()
    {
        var encoded = TomlStringEncoder.EncodeMultilineBasicString("first\nsecond\tindented");

        encoded.Should().Be("\"\"\"\nfirst\nsecond\tindented\"\"\"");
    }

    [Test]
    public void EncodeMultilineBasicString_BreaksUpARunOfThreeQuotes()
    {
        // Three consecutive quotes inside the content would close the string early.
        var encoded = TomlStringEncoder.EncodeMultilineBasicString("a\"\"\"b");

        encoded.Should().Be("\"\"\"\na\"\"\\\"b\"\"\"");
    }

    [Test]
    public void EncodeMultilineBasicString_BreaksAQuoteEndingTheValue()
    {
        // A trailing quote would otherwise merge with the closing delimiter into a run of four.
        var encoded = TomlStringEncoder.EncodeMultilineBasicString("a\"");

        encoded.Should().Be("\"\"\"\na\\\"\"\"\"");
    }

    [TestCase("plain")]
    [TestCase("multi\nline\nvalue")]
    [TestCase("tab\tseparated")]
    [TestCase("a\"\"\"b")]
    [TestCase("four\"\"\"\"quotes")]
    [TestCase("ends with one quote\"")]
    [TestCase("ends with two quotes\"\"")]
    [TestCase("back\\slash")]
    [TestCase("control\u0001character")]
    [TestCase("delete\u007fcharacter")]
    [TestCase("backspace\bcharacter")]
    public void EncodeMultilineBasicString_RoundTripsThroughTheParser(string value)
    {
        ReadValue(TomlStringEncoder.EncodeMultilineBasicString(value)).Should().Be(value);
    }

    // Files written before the escaping was consolidated carry \uXXXX where a named escape is emitted now.
    // The reader is the same parser either way, so both forms still load.
    [TestCase("\"a\\u0009b\"")]
    [TestCase("\"a\\tb\"")]
    public void TheParser_ReadsBothTheOldAndTheNewEscapeForm(string literal)
    {
        ReadValue(literal).Should().Be("a\tb");
    }

    // Parses an encoded literal with the parser every one of these writers is read back by, so what is
    // asserted is that the value survives rather than that the bytes take a particular shape.
    private static string ReadValue(string encodedLiteral)
    {
        var table = TomlSerializer.Deserialize<TomlTable>($"value = {encodedLiteral}\n");
        table.Should().NotBeNull();

        return (string)table!["value"]!;
    }
}
