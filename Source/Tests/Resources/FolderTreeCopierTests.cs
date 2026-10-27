using Celbridge.Resources.Helpers;
using Celbridge.Tests.FileSystem;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests the folder tree copy, which composes the local file system's single-file copy.
/// </summary>
[TestFixture]
public class FolderTreeCopierTests
{
    private string _tempFolder = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(FolderTreeCopierTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, true);
        }
    }

    [Test]
    public async Task CopiesEveryFileAndFolderInTheTree()
    {
        var sourceFolder = Path.Combine(_tempFolder, "source");
        Directory.CreateDirectory(Path.Combine(sourceFolder, "nested", "deeper"));
        Directory.CreateDirectory(Path.Combine(sourceFolder, "empty"));
        await File.WriteAllTextAsync(Path.Combine(sourceFolder, "top.txt"), "top");
        await File.WriteAllTextAsync(Path.Combine(sourceFolder, "nested", "deeper", "deep.txt"), "deep");
        var destFolder = Path.Combine(_tempFolder, "dest");

        var result = await FolderTreeCopier.CopyAsync(TestFileSystem.CreateLocal(), sourceFolder, destFolder);

        result.IsSuccess.Should().BeTrue(result.DiagnosticReport);
        (await File.ReadAllTextAsync(Path.Combine(destFolder, "top.txt"))).Should().Be("top");
        (await File.ReadAllTextAsync(Path.Combine(destFolder, "nested", "deeper", "deep.txt"))).Should().Be("deep");
        Directory.Exists(Path.Combine(destFolder, "empty")).Should().BeTrue();
        File.Exists(Path.Combine(sourceFolder, "top.txt")).Should().BeTrue();
    }

    [Test]
    public async Task FailsForAMissingSource()
    {
        var sourceFolder = Path.Combine(_tempFolder, "missing");
        var destFolder = Path.Combine(_tempFolder, "dest");

        var result = await FolderTreeCopier.CopyAsync(TestFileSystem.CreateLocal(), sourceFolder, destFolder);

        result.IsFailure.Should().BeTrue();
    }
}
