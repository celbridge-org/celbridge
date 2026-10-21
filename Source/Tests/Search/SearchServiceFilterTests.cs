using Celbridge.Automation;
using Celbridge.FileSystem.Services;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Search.Services;
using Celbridge.Tests.Packages;
using Celbridge.Tests.Migration.TestHelpers;
using Celbridge.Workspace;

namespace Celbridge.Tests.Search;

/// <summary>
/// Tests for the size and extension rules that decide whether a walked file is searched.
/// </summary>
[TestFixture]
public class SearchServiceFilterTests
{
    private SearchService _service = null!;

    [SetUp]
    public void SetUp()
    {
        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspaceLoaded.Returns(false);

        _service = new SearchService(
            Substitute.For<ILogger<SearchService>>(),
            workspaceWrapper,
            Substitute.For<IProjectService>(),
            TestFileTypeCatalog.CreateSniffer(new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>())),
            Substitute.For<Celbridge.UserInterface.Services.ISpotlightService>(),
            Substitute.For<IAutomationService>());
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();
    }

    [Test]
    public void ShouldSearchFile_RegularTextFile_ReturnsTrue()
    {
        _service.ShouldSearchFile(WalkedFile("test.txt", 12)).Should().BeTrue();
    }

    [Test]
    public void ShouldSearchFile_Folder_ReturnsFalse()
    {
        var folder = new WalkedItem(new ResourceKey("docs"), "/project/docs", true, 0, default, FileSystemAttributes.None);

        _service.ShouldSearchFile(folder).Should().BeFalse();
    }

    [Test]
    public void ShouldSearchFile_MetadataExtension_ReturnsFalse()
    {
        _service.ShouldSearchFile(WalkedFile("test.celbridge", 8)).Should().BeFalse();
    }

    [Test]
    public void ShouldSearchFile_CelExtension_ReturnsFalse()
    {
        // .cel sidecar files are excluded from plain-text search because their
        // content is editor-owned and a plain-text replace would corrupt the
        // TOML.
        _service.ShouldSearchFile(WalkedFile("photo.png.cel", 14)).Should().BeFalse();
    }

    [Test]
    public void ShouldSearchFile_CelExtensionWithMetadataFiles_ReturnsTrue()
    {
        _service.ShouldSearchFile(WalkedFile("photo.png.cel", 14), includeMetadataFiles: true).Should().BeTrue();
    }

    [Test]
    public void ShouldSearchFile_BinaryExtension_ReturnsFalse()
    {
        _service.ShouldSearchFile(WalkedFile("test.exe", 3)).Should().BeFalse();
    }

    [Test]
    public void ShouldSearchFile_ImageExtension_ReturnsFalse()
    {
        _service.ShouldSearchFile(WalkedFile("test.png", 4)).Should().BeFalse();
    }

    [Test]
    public void ShouldSearchFile_CSharpFile_ReturnsTrue()
    {
        _service.ShouldSearchFile(WalkedFile("Test.cs", 21)).Should().BeTrue();
    }

    [Test]
    public void ShouldSearchFile_MarkdownFile_ReturnsTrue()
    {
        _service.ShouldSearchFile(WalkedFile("README.md", 8)).Should().BeTrue();
    }

    [Test]
    public void ShouldSearchFile_LargeFile_ReturnsFalse()
    {
        _service.ShouldSearchFile(WalkedFile("large.txt", 1024 * 1024 + 1)).Should().BeFalse();
    }

    private static WalkedItem WalkedFile(string name, long size)
    {
        return new WalkedItem(new ResourceKey(name), "/project/" + name, false, size, default, FileSystemAttributes.None);
    }
}
