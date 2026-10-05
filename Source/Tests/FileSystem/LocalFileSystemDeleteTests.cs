namespace Celbridge.Tests.FileSystem;

/// <summary>
/// Tests for the deletes that run on a worker thread, and the failures they report.
/// </summary>
[TestFixture]
public class LocalFileSystemDeleteTests
{
    private string _root = string.Empty;
    private ILocalFileSystem _fileSystem = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"cel-delete-{Guid.NewGuid():N}");
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

    [Test]
    public async Task DeleteFolderAsync_RecursiveOnAFolderWithContent_RemovesIt()
    {
        var folderPath = Path.Combine(_root, "folder");
        Directory.CreateDirectory(folderPath);
        File.WriteAllText(Path.Combine(folderPath, "file.txt"), "content");

        var result = await _fileSystem.DeleteFolderAsync(folderPath, recursive: true);

        result.IsSuccess.Should().BeTrue();
        Directory.Exists(folderPath).Should().BeFalse();
    }

    [Test]
    public async Task DeleteFolderAsync_NonRecursiveOnAFolderWithContent_FailsWithTheOperationsException()
    {
        var folderPath = Path.Combine(_root, "folder");
        Directory.CreateDirectory(folderPath);
        File.WriteAllText(Path.Combine(folderPath, "file.txt"), "content");

        var result = await _fileSystem.DeleteFolderAsync(folderPath, recursive: false);

        result.IsFailure.Should().BeTrue();
        result.FirstException.Should().BeAssignableTo<IOException>();
        Directory.Exists(folderPath).Should().BeTrue();
    }
}
