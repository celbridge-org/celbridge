using Celbridge.Utilities;
using System.Text;

namespace Celbridge.Tests.Utilities;

[TestFixture]
public class TextBinarySnifferTests
{
    private string _testFilesDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testFilesDir = Path.Combine(Path.GetTempPath(), "TextBinarySnifferTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testFilesDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testFilesDir))
        {
            Directory.Delete(_testFilesDir, recursive: true);
        }
    }

    #region Binary Extension Tests

    [Test]
    public void IsBinaryExtension_KnownBinaryExtensions_ReturnsTrue()
    {
        TextBinarySniffer.IsBinaryExtension(".exe").Should().BeTrue();
        TextBinarySniffer.IsBinaryExtension(".dll").Should().BeTrue();
        TextBinarySniffer.IsBinaryExtension(".png").Should().BeTrue();
        TextBinarySniffer.IsBinaryExtension(".jpg").Should().BeTrue();
        TextBinarySniffer.IsBinaryExtension(".pdf").Should().BeTrue();
        TextBinarySniffer.IsBinaryExtension(".zip").Should().BeTrue();
    }

    [Test]
    public void IsBinaryExtension_TextExtensions_ReturnsFalse()
    {
        TextBinarySniffer.IsBinaryExtension(".txt").Should().BeFalse();
        TextBinarySniffer.IsBinaryExtension(".cs").Should().BeFalse();
        TextBinarySniffer.IsBinaryExtension(".json").Should().BeFalse();
        TextBinarySniffer.IsBinaryExtension(".xml").Should().BeFalse();
    }

    [Test]
    public void IsBinaryExtension_WithoutLeadingDot_ReturnsCorrectResult()
    {
        TextBinarySniffer.IsBinaryExtension("exe").Should().BeTrue();
        TextBinarySniffer.IsBinaryExtension("txt").Should().BeFalse();
    }

    [Test]
    public void IsBinaryExtension_CaseInsensitive_ReturnsCorrectResult()
    {
        TextBinarySniffer.IsBinaryExtension(".EXE").Should().BeTrue();
        TextBinarySniffer.IsBinaryExtension(".Png").Should().BeTrue();
    }

    [Test]
    public void IsBinaryExtension_SVG_IsBinary()
    {
        // As per design doc: SVG treated as binary (opened in WebView2, not edited as text)
        TextBinarySniffer.IsBinaryExtension(".svg").Should().BeTrue();
    }

    #endregion

    #region UTF-8 Tests

    [Test]
    public void IsTextFile_UTF8WithBOM_ReturnsTrue()
    {
        var filePath = Path.Combine(_testFilesDir, "utf8-bom.txt");
        var content = "Hello, World! 你好世界";
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(content)).ToArray();
        File.WriteAllBytes(filePath, bytes);

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Test]
    public void IsTextFile_UTF8WithoutBOM_ReturnsTrue()
    {
        var filePath = Path.Combine(_testFilesDir, "utf8-no-bom.txt");
        var content = "Hello, World! 你好世界 Привет мир";
        File.WriteAllText(filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Test]
    public void IsTextFile_PlainASCII_ReturnsTrue()
    {
        var filePath = Path.Combine(_testFilesDir, "ascii.txt");
        File.WriteAllText(filePath, "Hello World\nThis is a test\n");

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Test]
    public void IsTextFile_ASCIIWithTabs_ReturnsTrue()
    {
        var filePath = Path.Combine(_testFilesDir, "tabs.txt");
        File.WriteAllText(filePath, "Column1\tColumn2\tColumn3\nValue1\tValue2\tValue3\n");

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Test]
    public void IsTextFile_WithANSIEscapeCodes_ReturnsTrue()
    {
        var filePath = Path.Combine(_testFilesDir, "ansi.txt");
        // ANSI escape codes for colored terminal output
        var content = "\x1b[31mRed Text\x1b[0m\n\x1b[32mGreen Text\x1b[0m";
        File.WriteAllText(filePath, content);

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    #endregion

    #region UTF-16 Tests (Key scenarios from design doc)

    [Test]
    public void IsTextFile_UTF16LEWithBOM_ReturnsTrue()
    {
        var filePath = Path.Combine(_testFilesDir, "utf16le-bom.txt");
        var content = "Hello World";
        File.WriteAllText(filePath, content, Encoding.Unicode); // UTF-16 LE with BOM

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue("UTF-16 with BOM should be detected as text");
    }

    [Test]
    public void IsTextFile_UTF16BEWithBOM_ReturnsTrue()
    {
        var filePath = Path.Combine(_testFilesDir, "utf16be-bom.txt");
        var content = "Hello World";
        File.WriteAllText(filePath, content, Encoding.BigEndianUnicode); // UTF-16 BE with BOM

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue("UTF-16 BE with BOM should be detected as text");
    }

    [Test]
    public void IsTextFile_UTF16BEWithoutBOM_ReturnsTrue()
    {
        // UTF-16 BE without BOM should also be detected as text
        var filePath = Path.Combine(_testFilesDir, "utf16be-no-bom.txt");
        var content = "Hello World";
        var bytes = Encoding.BigEndianUnicode.GetBytes(content);
        
        // Write without BOM
        var noBomBytes = bytes.Skip(Encoding.BigEndianUnicode.GetPreamble().Length).ToArray();
        File.WriteAllBytes(filePath, noBomBytes);

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue("UTF-16 BE without BOM is a valid text encoding");
    }

    [Test]
    public void IsTextFile_UTF16LEWithoutBOM_ReturnsTrue()
    {
        // UTF-16 LE without BOM is a valid text encoding used by Windows tools
        var filePath = Path.Combine(_testFilesDir, "utf16le-no-bom.txt");
        var content = "Hello World"; // In UTF-16 LE: 48 00 65 00 6C 00 6C 00 6F 00...
        var bytes = Encoding.Unicode.GetBytes(content);
        
        // Write without BOM (skip the BOM that Encoding.Unicode normally adds)
        var noBomBytes = bytes.Skip(Encoding.Unicode.GetPreamble().Length).ToArray();
        File.WriteAllBytes(filePath, noBomBytes);

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue("UTF-16 LE without BOM is a valid text encoding");
    }

    #endregion

    #region UTF-32 Tests

    [Test]
    public void IsTextFile_UTF32LEWithBOM_ReturnsTrue()
    {
        var filePath = Path.Combine(_testFilesDir, "utf32le-bom.txt");
        var content = "Hello";
        File.WriteAllText(filePath, content, Encoding.UTF32); // UTF-32 LE with BOM

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue("UTF-32 with BOM should be detected as text");
    }

    [Test]
    public void IsTextFile_UTF32WithoutBOM_ReturnsTrue()
    {
        // UTF-32 without BOM should be detected as text
        var filePath = Path.Combine(_testFilesDir, "utf32-no-bom.txt");
        var content = "Hello World";
        var bytes = Encoding.UTF32.GetBytes(content);
        
        // Write without BOM
        var noBomBytes = bytes.Skip(Encoding.UTF32.GetPreamble().Length).ToArray();
        File.WriteAllBytes(filePath, noBomBytes);

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue("UTF-32 without BOM is a valid text encoding");
    }

    #endregion

    #region Legacy Encoding Tests

    [Test]
    public void IsTextFile_Latin1Text_ReturnsTrue()
    {
        var filePath = Path.Combine(_testFilesDir, "latin1.txt");
        // Latin-1 text with accented characters
        var content = "Café résumé naïve";
        var bytes = Encoding.Latin1.GetBytes(content);
        File.WriteAllBytes(filePath, bytes);

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue("Legacy 8-bit encodings like Latin-1 should be detected as text");
    }

    // Note: Skipping Windows-1252 test as it requires registering a code page provider in .NET Core/9

    #endregion

    #region Binary File Tests

    [Test]
    public void IsTextFile_BinaryFileWithNULBytes_ReturnsFalse()
    {
        var filePath = Path.Combine(_testFilesDir, "binary.bin");
        // Simulate binary data with NUL bytes
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE, 0x00, 0x00 };
        File.WriteAllBytes(filePath, bytes);

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse("Binary data with NUL bytes should be detected as binary");
    }

    [Test]
    public void IsTextFile_HighControlCharacterRatio_ReturnsFalse()
    {
        var filePath = Path.Combine(_testFilesDir, "mostly-control.bin");
        // Create content with >2% suspicious control characters
        var bytes = new byte[1000];
        for (int i = 0; i < 1000; i++)
        {
            if (i < 30) // 3% control characters (above 2% threshold)
            {
                bytes[i] = 0x01; // Suspicious control char
            }
            else
            {
                bytes[i] = (byte)'A';
            }
        }
        File.WriteAllBytes(filePath, bytes);

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse("Content with >2% suspicious control characters should be binary");
    }

    [Test]
    public void IsTextFile_JustBelowThreshold_ReturnsTrue()
    {
        var filePath = Path.Combine(_testFilesDir, "just-below-threshold.txt");
        // Create content with exactly 2% suspicious control characters (at threshold)
        var bytes = new byte[1000];
        for (int i = 0; i < 1000; i++)
        {
            if (i < 20) // Exactly 2%
            {
                bytes[i] = 0x01; // Suspicious control char
            }
            else
            {
                bytes[i] = (byte)'A';
            }
        }
        File.WriteAllBytes(filePath, bytes);

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue("Content with exactly 2% control characters should pass (at threshold)");
    }

    #endregion

    #region Edge Cases

    [Test]
    public void IsTextFile_EmptyFile_ReturnsTrue()
    {
        var filePath = Path.Combine(_testFilesDir, "empty.txt");
        File.WriteAllText(filePath, string.Empty);

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue("Empty files should be treated as text");
    }

    [Test]
    public void IsTextFile_VerySmallFile_Works()
    {
        var filePath = Path.Combine(_testFilesDir, "tiny.txt");
        File.WriteAllText(filePath, "Hi");

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Test]
    public void IsTextFile_NonExistentFile_ReturnsFailure()
    {
        var filePath = Path.Combine(_testFilesDir, "does-not-exist.txt");

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeFalse();
        result.FirstErrorMessage.Should().Contain("does not exist");
    }

    [Test]
    public void IsTextFile_NullPath_ReturnsFailure()
    {
        var result = TextBinarySniffer.IsTextFile(null!);

        result.IsSuccess.Should().BeFalse();
        result.FirstErrorMessage.Should().Contain("null or empty");
    }

    #endregion

    #region IsTextContent Tests

    [Test]
    public void IsTextContent_PlainText_ReturnsTrue()
    {
        var result = TextBinarySniffer.IsTextContent("Hello, World!");

        result.Should().BeTrue();
    }

    [Test]
    public void IsTextContent_EmptyString_ReturnsTrue()
    {
        var result = TextBinarySniffer.IsTextContent(string.Empty);

        result.Should().BeTrue();
    }

    [Test]
    public void IsTextContent_NullString_ReturnsTrue()
    {
        var result = TextBinarySniffer.IsTextContent(null!);

        result.Should().BeTrue();
    }

    [Test]
    public void IsTextContent_Unicode_ReturnsTrue()
    {
        var result = TextBinarySniffer.IsTextContent("Hello 世界 🌍");

        result.Should().BeTrue();
    }

    #endregion

    #region Real-World File Format Tests

    [Test]
    public void IsTextFile_CSharpSourceFile_ReturnsTrue()
    {
        var filePath = Path.Combine(_testFilesDir, "Program.cs");
        var content = @"using System;

namespace MyApp
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(""Hello, World!"");
        }
    }
}";
        File.WriteAllText(filePath, content);

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Test]
    public void IsTextFile_JSONFile_ReturnsTrue()
    {
        var filePath = Path.Combine(_testFilesDir, "data.json");
        var content = @"{
  ""name"": ""John Doe"",
  ""age"": 30,
  ""city"": ""New York""
}";
        File.WriteAllText(filePath, content);

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Test]
    public void IsTextFile_XMLFile_ReturnsTrue()
    {
        var filePath = Path.Combine(_testFilesDir, "data.xml");
        var content = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<root>
  <item>Value</item>
</root>";
        File.WriteAllText(filePath, content);

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Test]
    public void IsTextFile_MarkdownFile_ReturnsTrue()
    {
        var filePath = Path.Combine(_testFilesDir, "README.md");
        var content = @"# Title

This is a **markdown** file with *formatting*.

- Item 1
- Item 2
";
        File.WriteAllText(filePath, content);

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Test]
    public void IsTextFile_WindowsLineEndings_ReturnsTrue()
    {
        var filePath = Path.Combine(_testFilesDir, "windows.txt");
        var content = "Line 1\r\nLine 2\r\nLine 3\r\n";
        File.WriteAllText(filePath, content);

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Test]
    public void IsTextFile_UnixLineEndings_ReturnsTrue()
    {
        var filePath = Path.Combine(_testFilesDir, "unix.txt");
        var content = "Line 1\nLine 2\nLine 3\n";
        File.WriteAllText(filePath, content);

        var result = TextBinarySniffer.IsTextFile(filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    #endregion
}
