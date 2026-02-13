using Celbridge.Search;

namespace Celbridge.Tests.Search;

[TestFixture]
public class FileFilterTests
{
    private FileFilter _filter = null!;
    private string _testDir = null!;

    [SetUp]
    public void SetUp()
    {
        _filter = new FileFilter();
        _testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    [Test]
    public void ShouldSearchFile_RegularTextFile_ReturnsTrue()
    {
        var filePath = Path.Combine(_testDir, "test.txt");
        File.WriteAllText(filePath, "test content");

        _filter.ShouldSearchFile(filePath).Should().BeTrue();
    }

    [Test]
    public void ShouldSearchFile_NonExistentFile_ReturnsFalse()
    {
        var filePath = Path.Combine(_testDir, "nonexistent.txt");

        _filter.ShouldSearchFile(filePath).Should().BeFalse();
    }

    [Test]
    public void ShouldSearchFile_MetadataExtension_ReturnsFalse()
    {
        var filePath = Path.Combine(_testDir, "test.celbridge");
        File.WriteAllText(filePath, "metadata");

        _filter.ShouldSearchFile(filePath).Should().BeFalse();
    }

    [Test]
    public void ShouldSearchFile_WebappExtension_ReturnsFalse()
    {
        var filePath = Path.Combine(_testDir, "test.webapp");
        File.WriteAllText(filePath, "webapp data");

        _filter.ShouldSearchFile(filePath).Should().BeFalse();
    }

    [Test]
    public void ShouldSearchFile_BinaryExtension_ReturnsFalse()
    {
        var filePath = Path.Combine(_testDir, "test.exe");
        File.WriteAllBytes(filePath, new byte[] { 0x00, 0x01, 0x02 });

        _filter.ShouldSearchFile(filePath).Should().BeFalse();
    }

    [Test]
    public void ShouldSearchFile_ImageExtension_ReturnsFalse()
    {
        var filePath = Path.Combine(_testDir, "test.png");
        File.WriteAllBytes(filePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        _filter.ShouldSearchFile(filePath).Should().BeFalse();
    }

    [Test]
    public void ShouldSearchFile_CSharpFile_ReturnsTrue()
    {
        var filePath = Path.Combine(_testDir, "Test.cs");
        File.WriteAllText(filePath, "public class Test { }");

        _filter.ShouldSearchFile(filePath).Should().BeTrue();
    }

    [Test]
    public void ShouldSearchFile_MarkdownFile_ReturnsTrue()
    {
        var filePath = Path.Combine(_testDir, "README.md");
        File.WriteAllText(filePath, "# Readme");

        _filter.ShouldSearchFile(filePath).Should().BeTrue();
    }

    [Test]
    public void ShouldSearchFile_LargeFile_ReturnsFalse()
    {
        var filePath = Path.Combine(_testDir, "large.txt");
        // Create a file larger than 1MB
        using (var fs = File.Create(filePath))
        {
            fs.SetLength(1024 * 1024 + 1);
        }

        _filter.ShouldSearchFile(filePath).Should().BeFalse();
    }

    [Test]
    public void IsTextContent_NormalText_ReturnsTrue()
    {
        var content = "This is normal text content";

        _filter.IsTextContent(content).Should().BeTrue();
    }

    [Test]
    public void IsTextContent_WithNullCharacter_ReturnsFalse()
    {
        var content = "Text with \0 null character";

        _filter.IsTextContent(content).Should().BeFalse();
    }

    [Test]
    public void IsTextContent_EmptyString_ReturnsTrue()
    {
        var content = "";

        _filter.IsTextContent(content).Should().BeTrue();
    }

    [Test]
    public void IsTextContent_Unicode_ReturnsTrue()
    {
        var content = "Text with Unicode: ñ, ü, 中文, 日本語, 한글";

        _filter.IsTextContent(content).Should().BeTrue();
    }
}
