using Celbridge.FileSystem.Services;
using Celbridge.Logging;

namespace Celbridge.Tests.FileSystem;

[TestFixture]
public class FileSystemMonitorTests
{
    private string _tempFolder = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(FileSystemMonitorTests),
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

    private FileSystemMonitor CreateMonitor(string backingFolderPath)
    {
        return new FileSystemMonitor(Substitute.For<ILogger<FileSystemMonitor>>(), backingFolderPath);
    }

    [Test]
    public void Start_Succeeds_OverExistingFolder()
    {
        var monitor = CreateMonitor(_tempFolder);

        monitor.Start().IsSuccess.Should().BeTrue();

        monitor.Dispose();
    }

    [Test]
    public void Start_Fails_WhenBackingFolderMissing()
    {
        var missingFolder = Path.Combine(_tempFolder, "does-not-exist");
        var monitor = CreateMonitor(missingFolder);

        monitor.Start().IsFailure.Should().BeTrue();

        monitor.Dispose();
    }

    [Test]
    public void Dispose_IsIdempotent()
    {
        var monitor = CreateMonitor(_tempFolder);
        monitor.Start().IsSuccess.Should().BeTrue();

        monitor.Dispose();
        Action secondDispose = () => monitor.Dispose();

        secondDispose.Should().NotThrow();
    }

    [Test]
    public void Start_AfterDispose_Fails()
    {
        var monitor = CreateMonitor(_tempFolder);
        monitor.Dispose();

        monitor.Start().IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task Dispose_DuringActiveChangedBurst_DoesNotThrow()
    {
        var monitor = CreateMonitor(_tempFolder);
        monitor.Start().IsSuccess.Should().BeTrue();

        // Churn one file so Changed-debounce timers are repeatedly created and
        // reset while Dispose tears them down, exercising the dispose/timer race
        // and the post-dispose Raise guard.
        var filePath = Path.Combine(_tempFolder, "churn.txt");
        var writer = Task.Run(async () =>
        {
            for (int i = 0; i < 50; i++)
            {
                try
                {
                    await File.WriteAllTextAsync(filePath, i.ToString());
                }
                catch (IOException)
                {
                    // The backing folder may be removed by TearDown timing.
                }
            }
        });

        await Task.Delay(20);

        Action dispose = () => monitor.Dispose();
        dispose.Should().NotThrow();

        await writer;
    }

    [Test]
    public async Task FileSystemChanged_RaisesNoEvents_AfterDispose()
    {
        var monitor = CreateMonitor(_tempFolder);
        var disposed = false;
        var eventsAfterDispose = 0;
        monitor.FileSystemChanged += (_, _) =>
        {
            if (Volatile.Read(ref disposed))
            {
                Interlocked.Increment(ref eventsAfterDispose);
            }
        };
        monitor.Start().IsSuccess.Should().BeTrue();

        for (int i = 0; i < 5; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(_tempFolder, $"file{i}.txt"), "x");
        }

        Volatile.Write(ref disposed, true);
        monitor.Dispose();

        // Wait beyond the 75ms Changed-debounce window; the disposed-state guards
        // must drop any watcher callback still in flight.
        await Task.Delay(200);

        eventsAfterDispose.Should().Be(0);
    }
}
