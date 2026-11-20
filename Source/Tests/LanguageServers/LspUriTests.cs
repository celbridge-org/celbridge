using Celbridge.LanguageServers.Helpers;

namespace Celbridge.Tests.LanguageServers;

/// <summary>
/// Covers the conversion between a file path and a language server's file URI.
/// </summary>
[TestFixture]
public class LspUriTests
{
    [Test]
    public void FromPath_AWindowsPath_LowercasesTheDriveAndEncodesTheColon()
    {
        var uri = LspUri.FromPath(@"D:\Projects\Acme\src\main.ts");

        uri.Should().Be("file:///d%3A/Projects/Acme/src/main.ts");
    }

    [Test]
    public void FromPath_AUnixPath_KeepsTheLetterCase()
    {
        var uri = LspUri.FromPath("/Users/acme/Projects/Acme/main.ts");

        uri.Should().Be("file:///Users/acme/Projects/Acme/main.ts");
    }

    [Test]
    public void FromPath_APathWithSpacesAndSymbols_EncodesEachSegment()
    {
        var uri = LspUri.FromPath("/Users/acme/My Project/a#b (1).ts");

        uri.Should().Be("file:///Users/acme/My%20Project/a%23b%20%281%29.ts");
    }

    [Test]
    public void ToPath_AUriWithAnEncodedDrive_ReturnsTheWindowsPath()
    {
        var path = LspUri.ToPath("file:///d%3A/Projects/Acme/src/main.ts");

        path.Should().Be(@"d:\Projects\Acme\src\main.ts");
    }

    [Test]
    public void ToPath_AUnixUri_ReturnsThePath()
    {
        var path = LspUri.ToPath("file:///Users/acme/My%20Project/main.ts");

        path.Should().Be("/Users/acme/My Project/main.ts");
    }

    [Test]
    public void ToPath_AnotherScheme_ReturnsAnEmptyString()
    {
        var path = LspUri.ToPath("untitled:Untitled-1");

        path.Should().BeEmpty();
    }

    [Test]
    public void AreSameFile_TwoWritingsOfOneDrivePath_ReturnsTrue()
    {
        var areSame = LspUri.AreSameFile("file:///d%3A/Projects/Acme/main.ts", "file:///d:/Projects/Acme/main.ts");

        areSame.Should().BeTrue();
    }

    [Test]
    public void AreSameFile_TwoFiles_ReturnsFalse()
    {
        var areSame = LspUri.AreSameFile("file:///d%3A/Projects/Acme/main.ts", "file:///d%3A/Projects/Acme/other.ts");

        areSame.Should().BeFalse();
    }
}
