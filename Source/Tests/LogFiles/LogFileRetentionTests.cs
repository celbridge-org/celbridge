// Not namespace Celbridge.Tests.Logging: that would shadow the Celbridge.Logging namespace for the
// test files that reach for it with a bare Logging. prefix.
namespace Celbridge.Tests.LogFiles;

/// <summary>
/// Unit tests for LogFileRetention — the startup sweep that bounds the number of
/// application log files, given that every run writes a new timestamped file.
/// The tests pin which files are considered, the newest-wins ordering, and the
/// guarantee that the starting run's own log file survives.
/// </summary>
[TestFixture]
public class LogFileRetentionTests
{
    private string _logFolderPath = null!;

    [SetUp]
    public void Setup()
    {
        _logFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(LogFileRetentionTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_logFolderPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_logFolderPath))
        {
            try
            {
                Directory.Delete(_logFolderPath, recursive: true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    [Test]
    public void DeleteOldLogFiles_DeletesOldestBeyondRetainedCount()
    {
        CreateLogFile("celbridge_20260101T000000Z.log");
        CreateLogFile("celbridge_20260102T000000Z.log");
        CreateLogFile("celbridge_20260103T000000Z.log");
        var currentLogFilePath = Path.Combine(_logFolderPath, "celbridge_20260104T000000Z.log");

        LogFileRetention.DeleteOldLogFiles(_logFolderPath, currentLogFilePath, retainedFileCount: 2);

        var remainingFileNames = GetRemainingFileNames();
        remainingFileNames.Should().BeEquivalentTo(new[]
        {
            "celbridge_20260102T000000Z.log",
            "celbridge_20260103T000000Z.log",
        });
    }

    [Test]
    public void DeleteOldLogFiles_KeepsCurrentRunLogFile()
    {
        // The starting run's file is excluded from the sweep even when it already exists on disk, so a
        // retained count of zero still leaves this run somewhere to write.
        var currentLogFilePath = CreateLogFile("celbridge_20260101T000000Z.log");
        CreateLogFile("celbridge_20260102T000000Z.log");

        LogFileRetention.DeleteOldLogFiles(_logFolderPath, currentLogFilePath, retainedFileCount: 0);

        var remainingFileNames = GetRemainingFileNames();
        remainingFileNames.Should().Equal("celbridge_20260101T000000Z.log");
    }

    [Test]
    public void DeleteOldLogFiles_UnderRetainedCount_DeletesNothing()
    {
        CreateLogFile("celbridge_20260101T000000Z.log");
        CreateLogFile("celbridge_20260102T000000Z.log");
        var currentLogFilePath = Path.Combine(_logFolderPath, "celbridge_20260103T000000Z.log");

        LogFileRetention.DeleteOldLogFiles(_logFolderPath, currentLogFilePath, retainedFileCount: 50);

        var remainingFileNames = GetRemainingFileNames();
        remainingFileNames.Should().HaveCount(2);
    }

    [Test]
    public void DeleteOldLogFiles_LeavesUnrelatedFilesAlone()
    {
        // The log folder is Celbridge's own, but the sweep only claims files it recognises as
        // application logs so anything else dropped in there survives.
        CreateLogFile("celbridge_20260101T000000Z.log");
        CreateLogFile("notes.txt");
        CreateLogFile("python_20260101T000000Z.log");
        var currentLogFilePath = Path.Combine(_logFolderPath, "celbridge_20260102T000000Z.log");

        LogFileRetention.DeleteOldLogFiles(_logFolderPath, currentLogFilePath, retainedFileCount: 0);

        var remainingFileNames = GetRemainingFileNames();
        remainingFileNames.Should().BeEquivalentTo(new[]
        {
            "notes.txt",
            "python_20260101T000000Z.log",
        });
    }

    [Test]
    public void DeleteOldLogFiles_MissingFolder_DoesNotThrow()
    {
        // First run on a clean machine: NLog creates the folder later, when it opens the log file.
        var missingFolderPath = Path.Combine(_logFolderPath, "does-not-exist");
        var currentLogFilePath = Path.Combine(missingFolderPath, "celbridge_20260101T000000Z.log");

        var sweep = () => LogFileRetention.DeleteOldLogFiles(missingFolderPath, currentLogFilePath, retainedFileCount: 0);

        sweep.Should().NotThrow();
    }

    private string CreateLogFile(string fileName)
    {
        var filePath = Path.Combine(_logFolderPath, fileName);
        File.WriteAllText(filePath, "log contents");

        return filePath;
    }

    private List<string> GetRemainingFileNames()
    {
        var filePaths = Directory.GetFiles(_logFolderPath);

        var fileNames = new List<string>();
        foreach (var filePath in filePaths)
        {
            fileNames.Add(Path.GetFileName(filePath));
        }
        fileNames.Sort(StringComparer.Ordinal);

        return fileNames;
    }
}
