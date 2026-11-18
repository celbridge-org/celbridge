using Celbridge.FileSystem;

namespace Celbridge.Tests.FileSystem;

/// <summary>
/// Tests for reading the kind of the item at a path through the filesystem gateway. An empty path, a missing path
/// and a path that cannot be read all report NotFound.
/// </summary>
[TestFixture]
public class LocalFileSystemKindTests
{
    private string _rootFolderPath = null!;
    private ILocalFileSystem _fileSystem = null!;

    [SetUp]
    public void Setup()
    {
        _rootFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(LocalFileSystemKindTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootFolderPath);

        _fileSystem = TestFileSystem.CreateLocal();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_rootFolderPath))
        {
            try
            {
                Directory.Delete(_rootFolderPath, true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    [Test]
    public async Task AFile_ReportsFile()
    {
        var filePath = Path.Combine(_rootFolderPath, "notes.txt");
        File.WriteAllText(filePath, "text");

        var kind = await _fileSystem.GetKindAsync(filePath);

        kind.Should().Be(StorageItemKind.File);
    }

    [Test]
    public async Task AFolder_ReportsFolder()
    {
        var folderPath = Path.Combine(_rootFolderPath, "folder");
        Directory.CreateDirectory(folderPath);

        var kind = await _fileSystem.GetKindAsync(folderPath);

        kind.Should().Be(StorageItemKind.Folder);
    }

    [Test]
    public async Task AMissingPath_ReportsNotFound()
    {
        var missingPath = Path.Combine(_rootFolderPath, "missing");

        var kind = await _fileSystem.GetKindAsync(missingPath);

        kind.Should().Be(StorageItemKind.NotFound);
    }

    [Test]
    public async Task AnEmptyPath_ReportsNotFound()
    {
        var kind = await _fileSystem.GetKindAsync(string.Empty);

        kind.Should().Be(StorageItemKind.NotFound);
    }

    [Test]
    public async Task APathThatCannotBeRead_ReportsNotFound()
    {
        // A null character is invalid in a path on every platform, so reading the path fails.
        var invalidPath = Path.Combine(_rootFolderPath, "bad\0name");

        var kind = await _fileSystem.GetKindAsync(invalidPath);

        kind.Should().Be(StorageItemKind.NotFound);
    }
}
