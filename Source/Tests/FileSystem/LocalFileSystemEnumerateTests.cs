using Celbridge.FileSystem;

namespace Celbridge.Tests.FileSystem;

/// <summary>
/// Tests for enumerating a folder through the filesystem gateway. A project acquires its reports,
/// trash and package folders lazily, so asking about one that does not exist yet is routine and must
/// not cost an exception. A check for an entry stops reading at the first entry it accepts.
/// </summary>
[TestFixture]
public class LocalFileSystemEnumerateTests
{
    private string _rootFolderPath = null!;
    private ILocalFileSystem _fileSystem = null!;

    [SetUp]
    public void Setup()
    {
        _rootFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(LocalFileSystemEnumerateTests),
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
    public async Task AMissingFolder_FailsWithoutThrowing()
    {
        var missingFolderPath = Path.Combine(_rootFolderPath, "history");

        var enumerateResult = await _fileSystem.EnumerateAsync(missingFolderPath, "*", recursive: false);

        enumerateResult.IsFailure.Should().BeTrue();

        // The probe is what keeps the OS from raising DirectoryNotFoundException, which a debugger
        // breaks on once per call and which showed up several times per project load.
        enumerateResult.FirstException.Should().BeNull();
    }

    [Test]
    public async Task AnExistingFolder_EnumeratesItsEntries()
    {
        File.WriteAllText(Path.Combine(_rootFolderPath, "one.report"), "{}");
        File.WriteAllText(Path.Combine(_rootFolderPath, "two.report"), "{}");
        File.WriteAllText(Path.Combine(_rootFolderPath, "notes.txt"), string.Empty);

        var enumerateResult = await _fileSystem.EnumerateAsync(_rootFolderPath, "*.report", recursive: false);

        enumerateResult.IsSuccess.Should().BeTrue();
        enumerateResult.Value.Should().HaveCount(2);
    }

    [Test]
    public async Task HasEntry_StopsReadingAtTheFirstAcceptedEntry()
    {
        File.WriteAllText(Path.Combine(_rootFolderPath, "one.txt"), string.Empty);
        File.WriteAllText(Path.Combine(_rootFolderPath, "two.txt"), string.Empty);
        Directory.CreateDirectory(Path.Combine(_rootFolderPath, "three"));
        var offeredCount = 0;

        var hasEntryResult = await _fileSystem.HasEntryAsync(_rootFolderPath, _ =>
        {
            offeredCount++;
            return true;
        });

        hasEntryResult.IsSuccess.Should().BeTrue();
        hasEntryResult.Value.Should().BeTrue();
        offeredCount.Should().Be(1);
    }

    [Test]
    public async Task HasEntry_OffersEveryEntry_WhenNoneIsAccepted()
    {
        File.WriteAllText(Path.Combine(_rootFolderPath, "one.txt"), string.Empty);
        Directory.CreateDirectory(Path.Combine(_rootFolderPath, "two"));
        var offeredKinds = new List<StorageItemKind>();

        var hasEntryResult = await _fileSystem.HasEntryAsync(_rootFolderPath, entry =>
        {
            offeredKinds.Add(entry.Kind);
            return false;
        });

        hasEntryResult.IsSuccess.Should().BeTrue();
        hasEntryResult.Value.Should().BeFalse();
        offeredKinds.Should().BeEquivalentTo([StorageItemKind.File, StorageItemKind.Folder]);
    }

    [Test]
    public async Task HasEntry_InAMissingFolder_FailsWithoutThrowing()
    {
        var missingFolderPath = Path.Combine(_rootFolderPath, "history");

        var hasEntryResult = await _fileSystem.HasEntryAsync(missingFolderPath, _ => true);

        hasEntryResult.IsFailure.Should().BeTrue();
        hasEntryResult.FirstException.Should().BeNull();
    }
}
