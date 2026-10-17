namespace Celbridge.Tests.FileSystem;

/// <summary>
/// Tests for how LinkHelper and LocalFileSystem treat links. They tell a link from a plain item and a usable
/// file from a broken link, and never walk into a linked folder.
/// </summary>
[TestFixture]
public class LocalFileSystemLinkTests
{
    private string _root = string.Empty;
    private ILocalFileSystem _fileSystem = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"cel-link-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _fileSystem = TestFileSystem.CreateLocal();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string WriteFile(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "content");
        return path;
    }

    [Test]
    public async Task GetInfoAsync_LinkToAFile_IsAFile()
    {
        var linkPath = Path.Combine(_root, "link");
        File.CreateSymbolicLink(linkPath, WriteFile("target.txt"));

        var result = await _fileSystem.GetInfoAsync(linkPath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be(StorageItemKind.File);
        result.Value.Size.Should().Be(7);
    }

    [Test]
    public async Task GetInfoAsync_LinkToAFolder_IsAFolder()
    {
        var folderPath = Path.Combine(_root, "folder");
        Directory.CreateDirectory(folderPath);
        var linkPath = Path.Combine(_root, "link");
        Directory.CreateSymbolicLink(linkPath, folderPath);

        var result = await _fileSystem.GetInfoAsync(linkPath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be(StorageItemKind.Folder);
    }

    [Test]
    public async Task GetInfoAsync_LinkWhoseTargetHasGone_IsABrokenLink()
    {
        // The case this kind exists for. The platform reports a dangling link as an existing file, so a
        // caller testing for File would open something that is not there.
        var targetPath = WriteFile("target.txt");
        var linkPath = Path.Combine(_root, "link");
        File.CreateSymbolicLink(linkPath, targetPath);
        File.Delete(targetPath);

        new FileInfo(linkPath).Exists.Should().BeTrue("the platform still reports the link as a file");

        var result = await _fileSystem.GetInfoAsync(linkPath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be(StorageItemKind.BrokenLink);
    }

    [Test]
    public async Task GetInfoAsync_LinkToALinkWhoseTargetHasGone_IsABrokenLink()
    {
        var targetPath = WriteFile("target.txt");
        var firstLink = Path.Combine(_root, "first");
        File.CreateSymbolicLink(firstLink, targetPath);
        var secondLink = Path.Combine(_root, "second");
        File.CreateSymbolicLink(secondLink, firstLink);
        File.Delete(targetPath);

        var result = await _fileSystem.GetInfoAsync(secondLink);

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be(StorageItemKind.BrokenLink);
    }

    [Test]
    public async Task GetInfoAsync_LinkToAFile_IsALink()
    {
        // Kind and size describe the target so a caller can use it, and the flag is the only thing left
        // saying a link is how they got there.
        var linkPath = Path.Combine(_root, "link");
        File.CreateSymbolicLink(linkPath, WriteFile("target.txt"));

        var result = await _fileSystem.GetInfoAsync(linkPath);

        result.Value.IsLink.Should().BeTrue();
    }

    [Test]
    public async Task GetInfoAsync_LinkToAFolder_IsALink()
    {
        var folderPath = Path.Combine(_root, "folder");
        Directory.CreateDirectory(folderPath);
        var linkPath = Path.Combine(_root, "link");
        Directory.CreateSymbolicLink(linkPath, folderPath);

        var result = await _fileSystem.GetInfoAsync(linkPath);

        result.Value.IsLink.Should().BeTrue();
    }

    [Test]
    public async Task GetInfoAsync_PlainFileAndFolder_AreNotLinks()
    {
        var folderPath = Path.Combine(_root, "folder");
        Directory.CreateDirectory(folderPath);

        var fileResult = await _fileSystem.GetInfoAsync(WriteFile("plain.txt"));
        var folderResult = await _fileSystem.GetInfoAsync(folderPath);

        fileResult.Value.IsLink.Should().BeFalse();
        folderResult.Value.IsLink.Should().BeFalse();
    }

    [Test]
    public async Task GetInfoAsync_BrokenLink_IsALink()
    {
        var targetPath = WriteFile("target.txt");
        var linkPath = Path.Combine(_root, "link");
        File.CreateSymbolicLink(linkPath, targetPath);
        File.Delete(targetPath);

        var result = await _fileSystem.GetInfoAsync(linkPath);

        result.Value.Kind.Should().Be(StorageItemKind.BrokenLink);
        result.Value.IsLink.Should().BeTrue();
    }

    [Test]
    public async Task EnumerateAsync_LinkToAFile_ReportsTheTargetsSize()
    {
        // The directory walk answers for the link, whose length is the path string it holds, so an
        // unresolved entry reports a size the file does not have.
        var targetPath = WriteFile("target.txt");
        File.CreateSymbolicLink(Path.Combine(_root, "link"), targetPath);

        var result = await _fileSystem.EnumerateAsync(_root, "*", recursive: false);

        var linkEntry = result.Value.Single(entry => entry.FullPath.EndsWith("link", StringComparison.Ordinal));
        linkEntry.Kind.Should().Be(StorageItemKind.File);
        linkEntry.Size.Should().Be(7);
        linkEntry.IsLink.Should().BeTrue();
    }

    [Test]
    public async Task EnumerateAsync_LinkWhoseTargetHasGone_IsABrokenLink()
    {
        var targetPath = WriteFile("target.txt");
        File.CreateSymbolicLink(Path.Combine(_root, "link"), targetPath);
        File.Delete(targetPath);

        var result = await _fileSystem.EnumerateAsync(_root, "*", recursive: false);

        var linkEntry = result.Value.Single(entry => entry.FullPath.EndsWith("link", StringComparison.Ordinal));
        linkEntry.Kind.Should().Be(StorageItemKind.BrokenLink);
        linkEntry.IsFolder.Should().BeFalse();
        linkEntry.IsLink.Should().BeTrue();
    }

    [Test]
    public async Task EnumerateAsync_LinkToAFolder_IsAFolder()
    {
        var folderPath = Path.Combine(_root, "folder");
        Directory.CreateDirectory(folderPath);
        Directory.CreateSymbolicLink(Path.Combine(_root, "link"), folderPath);

        var result = await _fileSystem.EnumerateAsync(_root, "*", recursive: false);

        var linkEntry = result.Value.Single(entry => entry.FullPath.EndsWith("link", StringComparison.Ordinal));
        linkEntry.IsFolder.Should().BeTrue();
        linkEntry.IsLink.Should().BeTrue();
    }

    [Test]
    public async Task EnumerateAsync_Recursive_ListsALinkedFolderWithoutDescendingIntoIt()
    {
        var outsidePath = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outsidePath);
        File.WriteAllText(Path.Combine(outsidePath, "secret.txt"), "secret");
        var walkedPath = Path.Combine(_root, "walked");
        Directory.CreateDirectory(Path.Combine(walkedPath, "nested"));
        File.WriteAllText(Path.Combine(walkedPath, "nested", "plain.txt"), "plain");
        Directory.CreateSymbolicLink(Path.Combine(walkedPath, "nested", "link"), outsidePath);

        var result = await _fileSystem.EnumerateAsync(walkedPath, "*", recursive: true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(entry => Path.GetRelativePath(walkedPath, entry.FullPath).Replace('\\', '/'))
            .Should().Equal("nested", "nested/link", "nested/plain.txt");
        result.Value.Single(entry => entry.FullPath.EndsWith("link", StringComparison.Ordinal)).IsLink.Should().BeTrue();
    }

    [Test]
    public async Task EnumerateAsync_Recursive_StopsAtALinkThatLoopsBack()
    {
        Directory.CreateDirectory(Path.Combine(_root, "nested"));
        WriteFile("plain.txt");
        Directory.CreateSymbolicLink(Path.Combine(_root, "nested", "loop"), _root);

        var result = await _fileSystem.EnumerateAsync(_root, "*", recursive: true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
    }

    [Test]
    public async Task EnumerateAsync_RecursiveWithAPattern_MatchesAtEveryDepth()
    {
        Directory.CreateDirectory(Path.Combine(_root, "a", "b"));
        WriteFile("top.js");
        File.WriteAllText(Path.Combine(_root, "a", "b", "deep.js"), "x");
        File.WriteAllText(Path.Combine(_root, "a", "skip.txt"), "x");

        var result = await _fileSystem.EnumerateAsync(_root, "*.js", recursive: true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(entry => Path.GetRelativePath(_root, entry.FullPath).Replace('\\', '/'))
            .Should().Equal("a/b/deep.js", "top.js");
    }

    [Test]
    public void IsLink_MissingPath_IsNotALink()
    {
        LinkHelper.IsLink(Path.Combine(_root, "absent.txt")).Should().BeFalse();
    }

    [Test]
    public async Task EnumerateAsync_PlainFile_AgreesWithGetInfoAsync()
    {
        var filePath = WriteFile("plain.txt");

        var enumerated = await _fileSystem.EnumerateAsync(_root, "*", recursive: false);
        var probed = await _fileSystem.GetInfoAsync(filePath);

        var entry = enumerated.Value.Single();
        entry.Kind.Should().Be(probed.Value.Kind);
        entry.Size.Should().Be(probed.Value.Size);
        entry.Attributes.Should().Be(probed.Value.Attributes);
    }

    [Test]
    public async Task GetInfoAsync_MissingPath_IsNotFound()
    {
        var result = await _fileSystem.GetInfoAsync(Path.Combine(_root, "absent.txt"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be(StorageItemKind.NotFound);
    }
}
