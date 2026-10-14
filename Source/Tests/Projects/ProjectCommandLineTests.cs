using Celbridge.Projects;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Tests how the project file named on the command line is found, and how Windows argument text is split.
/// </summary>
[TestFixture]
public class ProjectCommandLineTests
{
    private static readonly string WorkingFolderPath = Path.Combine(Path.GetTempPath(), "Working");

    [Test]
    public void FindProjectFile_OpensTheFirstProjectFileAndIgnoresOtherArguments()
    {
        var firstProjectFile = Path.Combine(Path.GetTempPath(), "First", "First.celbridge");
        var secondProjectFile = Path.Combine(Path.GetTempPath(), "Second", "Second.celbridge");
        var arguments = new List<string>
        {
            "--verbose",
            "notes.txt",
            firstProjectFile,
            secondProjectFile
        };

        var projectFile = ProjectCommandLine.FindProjectFile(arguments, WorkingFolderPath);

        projectFile.Should().Be(firstProjectFile);
    }

    [Test]
    public void FindProjectFile_RelativePath_IsTakenRelativeToTheWorkingFolder()
    {
        var relativePath = Path.Combine("Acme", "Acme.celbridge");
        var arguments = new List<string>
        {
            relativePath
        };

        var projectFile = ProjectCommandLine.FindProjectFile(arguments, WorkingFolderPath);

        projectFile.Should().Be(Path.Combine(WorkingFolderPath, "Acme", "Acme.celbridge"));
    }

    [Test]
    public void FindProjectFile_MatchesTheExtensionInAnyCase()
    {
        var upperCasePath = Path.Combine(WorkingFolderPath, "Acme.CELBRIDGE");
        var arguments = new List<string>
        {
            upperCasePath
        };

        var projectFile = ProjectCommandLine.FindProjectFile(arguments, WorkingFolderPath);

        projectFile.Should().Be(upperCasePath);
    }

    [Test]
    public void FindProjectFile_NoProjectFile_ReturnsNull()
    {
        var arguments = new List<string>
        {
            "--verbose",
            "notes.txt"
        };

        var projectFile = ProjectCommandLine.FindProjectFile(arguments, WorkingFolderPath);

        projectFile.Should().BeNull();
    }

    [Test]
    public void SplitWindowsArguments_SplitsOnSpacesAndTabs()
    {
        var arguments = ProjectCommandLine.SplitWindowsArguments("  one two\tthree  ");

        arguments.Should().Equal("one", "two", "three");
    }

    [Test]
    public void SplitWindowsArguments_QuotedPathWithSpaces_IsOneArgument()
    {
        var arguments = ProjectCommandLine.SplitWindowsArguments(@"""C:\My Projects\Acme\Acme.celbridge"" --verbose");

        arguments.Should().Equal(@"C:\My Projects\Acme\Acme.celbridge", "--verbose");
    }

    [Test]
    public void SplitWindowsArguments_BackslashesBeforeOtherCharacters_AreKept()
    {
        var arguments = ProjectCommandLine.SplitWindowsArguments(@"C:\Projects\Acme.celbridge \\server\share");

        arguments.Should().Equal(@"C:\Projects\Acme.celbridge", @"\\server\share");
    }

    [Test]
    public void SplitWindowsArguments_BackslashesBeforeAQuote_EscapeIt()
    {
        // A trailing backslash in a quoted folder is doubled. A backslash before a quote makes it literal.
        var arguments = ProjectCommandLine.SplitWindowsArguments(@"""C:\My Folder\\"" a\""b ""c""""d""");

        arguments.Should().Equal(@"C:\My Folder\", @"a""b", @"c""d");
    }

    [Test]
    public void SplitWindowsArguments_EmptyQuotes_AreAnEmptyArgument()
    {
        var arguments = ProjectCommandLine.SplitWindowsArguments(@"one """" two");

        arguments.Should().Equal("one", string.Empty, "two");
    }

    [Test]
    public void SplitWindowsArguments_EmptyText_HasNoArguments()
    {
        var arguments = ProjectCommandLine.SplitWindowsArguments(string.Empty);

        arguments.Should().BeEmpty();
    }
}
