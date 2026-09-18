namespace Celbridge.Tests.FileSystem;

/// <summary>
/// Tests for GetInfoAsync over symbolic links — telling a usable file from a link whose target has gone.
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
    public async Task GetInfoAsync_LinkToAFile_ReportsReparsePoint()
    {
        // Kind and size describe the target so a caller can use it, and the flag is the only thing left
        // saying a link is how they got there.
        var linkPath = Path.Combine(_root, "link");
        File.CreateSymbolicLink(linkPath, WriteFile("target.txt"));

        var result = await _fileSystem.GetInfoAsync(linkPath);

        result.Value.Attributes.HasFlag(FileSystemAttributes.ReparsePoint).Should().BeTrue();
    }

    [Test]
    public async Task GetInfoAsync_PlainFile_ReportsNoReparsePoint()
    {
        var result = await _fileSystem.GetInfoAsync(WriteFile("plain.txt"));

        result.Value.Attributes.HasFlag(FileSystemAttributes.ReparsePoint).Should().BeFalse();
    }

    [Test]
    public async Task GetInfoAsync_BrokenLink_ReportsReparsePoint()
    {
        var targetPath = WriteFile("target.txt");
        var linkPath = Path.Combine(_root, "link");
        File.CreateSymbolicLink(linkPath, targetPath);
        File.Delete(targetPath);

        var result = await _fileSystem.GetInfoAsync(linkPath);

        result.Value.Kind.Should().Be(StorageItemKind.BrokenLink);
        result.Value.Attributes.HasFlag(FileSystemAttributes.ReparsePoint).Should().BeTrue();
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
        linkEntry.Attributes.HasFlag(FileSystemAttributes.ReparsePoint).Should().BeTrue();
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
        linkEntry.Attributes.HasFlag(FileSystemAttributes.ReparsePoint).Should().BeTrue();
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
