using System.Text.RegularExpressions;
using Celbridge.Resources;
using Celbridge.Tools;
using Celbridge.Utilities;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for GlobHelper and FileToolHelpers — the pure logic helpers extracted
/// from the MCP file tool layer.
/// </summary>
[TestFixture]
public class FileToolHelpersTests
{
    //
    // GlobHelper.GlobToRegex — matches against a single name (no path separators)
    //

    [TestCase("*.cs", "foo.cs", true)]
    [TestCase("*.cs", "foo.py", false)]
    [TestCase("readme*", "readme", true)]
    [TestCase("readme*", "readme.md", true)]
    [TestCase("readme*", "readme.txt", true)]
    [TestCase("readme*", "README.md", false, TestName = "GlobToRegex_Star_CaseSensitiveByDefault")]
    [TestCase("readme.md", "readme.md", true)]
    [TestCase("readme.md", "readme.txt", false)]
    [TestCase("foo?.cs", "fooa.cs", true)]
    [TestCase("foo?.cs", "fooab.cs", false)]
    [TestCase("foo?.cs", "foo.cs", false, TestName = "GlobToRegex_Question_RequiresExactlyOneChar")]
    [TestCase("*", "anything", true)]
    [TestCase("*", "with.dot", true)]
    public void GlobToRegex_MatchesSingleName(string glob, string input, bool expectedMatch)
    {
        var pattern = GlobHelper.GlobToRegex(glob);
        var regex = new Regex(pattern);

        regex.IsMatch(input).Should().Be(expectedMatch);
    }

    [Test]
    public void GlobToRegex_IsNotCaseInsensitiveByDefault()
    {
        var pattern = GlobHelper.GlobToRegex("*.CS");
        var caseSensitiveRegex = new Regex(pattern);
        var caseInsensitiveRegex = new Regex(pattern, RegexOptions.IgnoreCase);

        caseSensitiveRegex.IsMatch("foo.cs").Should().BeFalse();
        caseInsensitiveRegex.IsMatch("foo.cs").Should().BeTrue();
    }

    //
    // GlobHelper.PathGlobToRegex — matches against a full resource key path
    //

    [TestCase("*.py", "foo.py", true)]
    [TestCase("*.py", "src/foo.py", false, TestName = "PathGlobToRegex_StarExtension_NoMatchAcrossSlash")]
    [TestCase("src/*.cs", "src/foo.cs", true)]
    [TestCase("src/*.cs", "src/sub/foo.cs", false, TestName = "PathGlobToRegex_Star_NoMatchAcrossSlash")]
    [TestCase("src/*.cs", "other/foo.cs", false)]
    [TestCase("**/foo.cs", "foo.cs", true, TestName = "PathGlobToRegex_DoubleStarSlash_MatchesTopLevel")]
    [TestCase("**/foo.cs", "src/foo.cs", true, TestName = "PathGlobToRegex_DoubleStarSlash_MatchesOneLevel")]
    [TestCase("**/foo.cs", "src/sub/foo.cs", true, TestName = "PathGlobToRegex_DoubleStarSlash_MatchesTwoLevels")]
    [TestCase("**/foo.cs", "src/foo.txt", false)]
    [TestCase("**/Commands/*.cs", "Commands/Foo.cs", true, TestName = "PathGlobToRegex_DoubleStarSlash_TopLevelFolder")]
    [TestCase("**/Commands/*.cs", "src/Commands/Foo.cs", true, TestName = "PathGlobToRegex_DoubleStarSlash_NestedFolder")]
    [TestCase("**/Commands/*.cs", "src/Commands/sub/Foo.cs", false, TestName = "PathGlobToRegex_DoubleStarSlash_DoesNotMatchAcrossSlashAfterStar")]
    [TestCase("Services/**/I*.cs", "Services/IFoo.cs", true, TestName = "PathGlobToRegex_DoubleStarMid_TopLevelFile")]
    [TestCase("Services/**/I*.cs", "Services/sub/IFoo.cs", true, TestName = "PathGlobToRegex_DoubleStarMid_NestedFile")]
    [TestCase("Services/**/I*.cs", "Services/sub/deep/IFoo.cs", true, TestName = "PathGlobToRegex_DoubleStarMid_DeepNestedFile")]
    [TestCase("Services/**/I*.cs", "Other/IFoo.cs", false)]
    [TestCase("**", "anything/at/all.cs", true)]
    [TestCase("src/**", "src/foo.cs", true)]
    [TestCase("src/**", "src/sub/foo.cs", true)]
    [TestCase("src/**", "other/foo.cs", false)]
    public void PathGlobToRegex_MatchesResourcePath(string glob, string input, bool expectedMatch)
    {
        var pattern = GlobHelper.PathGlobToRegex(glob);
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        regex.IsMatch(input).Should().Be(expectedMatch);
    }

    //
    // FileToolHelpers.BuildTree
    //

    [Test]
    public void BuildTree_EmptyFolder_ReturnsEmptyNode()
    {
        var root = MakeFolder("root");

        var result = FileToolHelpers.BuildTree(root, remainingDepth: 3, globRegex: null, typeFilter: "");

        result.Should().NotBeNull();
        result!.Name.Should().Be("root");
        result.Children.Should().BeEmpty();
        result.Truncated.Should().BeNull();
    }

    [Test]
    public void BuildTree_FlatFolder_ReturnsAllChildren()
    {
        var root = MakeFolder("root",
            MakeFile("foo.cs"),
            MakeFile("bar.cs"));

        var result = FileToolHelpers.BuildTree(root, remainingDepth: 3, globRegex: null, typeFilter: "");

        result!.Children.Should().HaveCount(2);
        var names = result.Children.Cast<TreeFileNode>().Select(n => n.Name).ToList();
        names.Should().Contain("foo.cs");
        names.Should().Contain("bar.cs");
    }

    [Test]
    public void BuildTree_DepthZero_FolderWithChildren_ReturnsTruncated()
    {
        var root = MakeFolder("root", MakeFile("foo.cs"));

        var result = FileToolHelpers.BuildTree(root, remainingDepth: 0, globRegex: null, typeFilter: "");

        result!.Children.Should().BeEmpty();
        result.Truncated.Should().BeTrue();
    }

    [Test]
    public void BuildTree_DepthZero_EmptyFolder_NotTruncated()
    {
        var root = MakeFolder("root");

        var result = FileToolHelpers.BuildTree(root, remainingDepth: 0, globRegex: null, typeFilter: "");

        result!.Children.Should().BeEmpty();
        result.Truncated.Should().BeNull();
    }

    [Test]
    public void BuildTree_DepthOne_SubfolderWithChildren_SubfolderIsTruncated()
    {
        var subFolder = MakeFolder("sub", MakeFile("nested.cs"));
        var root = MakeFolder("root", subFolder, MakeFile("top.cs"));

        var result = FileToolHelpers.BuildTree(root, remainingDepth: 1, globRegex: null, typeFilter: "");

        result!.Children.Should().HaveCount(2);
        var folderNode = result.Children.OfType<TreeFolderNode>().Single();
        folderNode.Name.Should().Be("sub");
        folderNode.Truncated.Should().BeTrue();
        folderNode.Children.Should().BeEmpty();
    }

    [Test]
    public void BuildTree_DepthTwo_NestedFolder_FullyExpanded()
    {
        var subFolder = MakeFolder("sub", MakeFile("nested.cs"));
        var root = MakeFolder("root", subFolder);

        var result = FileToolHelpers.BuildTree(root, remainingDepth: 2, globRegex: null, typeFilter: "");

        var folderNode = result!.Children.OfType<TreeFolderNode>().Single();
        folderNode.Truncated.Should().BeNull();
        folderNode.Children.Should().HaveCount(1);
        folderNode.Children.OfType<TreeFileNode>().Single().Name.Should().Be("nested.cs");
    }

    [Test]
    public void BuildTree_GlobFilter_OnlyMatchingFilesReturned()
    {
        var root = MakeFolder("root",
            MakeFile("foo.cs"),
            MakeFile("bar.py"),
            MakeFile("baz.cs"));

        var globPattern = GlobHelper.GlobToRegex("*.cs");
        var globRegex = new Regex(globPattern, RegexOptions.IgnoreCase);

        var result = FileToolHelpers.BuildTree(root, remainingDepth: 3, globRegex, typeFilter: "");

        result!.Children.Should().HaveCount(2);
        result.Children.Cast<TreeFileNode>().Select(n => n.Name).Should().BeEquivalentTo(new[] { "foo.cs", "baz.cs" });
    }

    [Test]
    public void BuildTree_GlobFilter_FolderWithNoMatchingDescendants_IsPruned()
    {
        var unmatchedFolder = MakeFolder("scripts", MakeFile("run.py"), MakeFile("test.py"));
        var matchedFolder = MakeFolder("src", MakeFile("foo.cs"));
        var root = MakeFolder("root", unmatchedFolder, matchedFolder);

        var globPattern = GlobHelper.GlobToRegex("*.cs");
        var globRegex = new Regex(globPattern, RegexOptions.IgnoreCase);

        var result = FileToolHelpers.BuildTree(root, remainingDepth: 3, globRegex, typeFilter: "");

        result!.Children.Should().HaveCount(1);
        result.Children.OfType<TreeFolderNode>().Single().Name.Should().Be("src");
    }

    [Test]
    public void BuildTree_GlobFilter_RootWithNoMatchingDescendants_ReturnsNull()
    {
        var root = MakeFolder("root", MakeFile("run.py"), MakeFile("test.py"));

        var globPattern = GlobHelper.GlobToRegex("*.cs");
        var globRegex = new Regex(globPattern, RegexOptions.IgnoreCase);

        var result = FileToolHelpers.BuildTree(root, remainingDepth: 3, globRegex, typeFilter: "");

        result.Should().BeNull();
    }

    [Test]
    public void BuildTree_TypeFilterFile_OnlyFileNodesReturned()
    {
        var subFolder = MakeFolder("sub", MakeFile("nested.cs"));
        var root = MakeFolder("root", subFolder, MakeFile("top.cs"));

        var result = FileToolHelpers.BuildTree(root, remainingDepth: 3, globRegex: null, typeFilter: "file");

        result!.Children.Should().HaveCount(1);
        result.Children.Single().Should().BeOfType<TreeFileNode>();
        ((TreeFileNode)result.Children.Single()).Name.Should().Be("top.cs");
    }

    [Test]
    public void BuildTree_TypeFilterFolder_OnlyFolderNodesReturned()
    {
        var subFolder = MakeFolder("sub", MakeFile("nested.cs"));
        var root = MakeFolder("root", subFolder, MakeFile("top.cs"));

        var result = FileToolHelpers.BuildTree(root, remainingDepth: 3, globRegex: null, typeFilter: "folder");

        result!.Children.Should().HaveCount(1);
        result.Children.Single().Should().BeOfType<TreeFolderNode>();
        ((TreeFolderNode)result.Children.Single()).Name.Should().Be("sub");
    }

    [Test]
    public void BuildTree_TypeFilterIsCaseInsensitive()
    {
        var subFolder = MakeFolder("sub");
        var root = MakeFolder("root", subFolder, MakeFile("top.cs"));

        var resultUpper = FileToolHelpers.BuildTree(root, remainingDepth: 3, globRegex: null, typeFilter: "FILE");
        var resultMixed = FileToolHelpers.BuildTree(root, remainingDepth: 3, globRegex: null, typeFilter: "Folder");

        resultUpper!.Children.Should().AllBeOfType<TreeFileNode>();
        resultMixed!.Children.Should().AllBeOfType<TreeFolderNode>();
    }

    //
    // Helpers for building test resource trees via NSubstitute
    //

    private static IFolderResource MakeFolder(string name, params IResource[] children)
    {
        var folder = Substitute.For<IFolderResource>();
        folder.Name.Returns(name);
        folder.Children.Returns(children.ToList<IResource>());
        return folder;
    }

    private static IResource MakeFile(string name)
    {
        var file = Substitute.For<IResource>();
        file.Name.Returns(name);
        return file;
    }
}
