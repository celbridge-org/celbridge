using Celbridge.Utilities;

namespace Celbridge.Tests.Utilities;

/// <summary>
/// Tests splitting Windows argument text into arguments.
/// </summary>
[TestFixture]
public class WindowsCommandLineTests
{
    [Test]
    public void Split_SplitsOnSpacesAndTabs()
    {
        var arguments = WindowsCommandLine.Split("  one two\tthree  ");

        arguments.Should().Equal("one", "two", "three");
    }

    [Test]
    public void Split_QuotedPathWithSpaces_IsOneArgument()
    {
        var arguments = WindowsCommandLine.Split(@"""C:\My Projects\Acme\Acme.celbridge"" --verbose");

        arguments.Should().Equal(@"C:\My Projects\Acme\Acme.celbridge", "--verbose");
    }

    [Test]
    public void Split_BackslashesBeforeOtherCharacters_AreKept()
    {
        var arguments = WindowsCommandLine.Split(@"C:\Projects\Acme.celbridge \\server\share");

        arguments.Should().Equal(@"C:\Projects\Acme.celbridge", @"\\server\share");
    }

    [Test]
    public void Split_BackslashesBeforeAQuote_EscapeIt()
    {
        // A trailing backslash in a quoted folder is doubled. A backslash before a quote makes it literal.
        var arguments = WindowsCommandLine.Split(@"""C:\My Folder\\"" a\""b ""c""""d""");

        arguments.Should().Equal(@"C:\My Folder\", @"a""b", @"c""d");
    }

    [Test]
    public void Split_EmptyQuotes_AreAnEmptyArgument()
    {
        var arguments = WindowsCommandLine.Split(@"one """" two");

        arguments.Should().Equal("one", string.Empty, "two");
    }

    [Test]
    public void Split_EmptyText_HasNoArguments()
    {
        var arguments = WindowsCommandLine.Split(string.Empty);

        arguments.Should().BeEmpty();
    }
}
