using Celbridge.FileSystem.Services;
using Celbridge.Settings.Services;
using Celbridge.Tests.Helpers;
using Celbridge.Tests.Migration.TestHelpers;

namespace Celbridge.Tests.Settings;

/// <summary>
/// Covers the deferred, gateway-backed Application settings store: round-trip
/// through a fresh load after flush, deferred durability (a write is lost without
/// a flush, survives with), removal, and tolerance of a corrupt or missing file.
/// Folder resolution is exercised through the static path helper.
/// </summary>
[TestFixture]
public class ApplicationStoreTests
{
    private string _folderPath = null!;
    private string _filePath = null!;
    private LocalFileSystem _fileSystem = null!;

    [SetUp]
    public void Setup()
    {
        _folderPath = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(ApplicationStoreTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folderPath);

        _filePath = Path.Combine(_folderPath, "settings.json");
        _fileSystem = new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_folderPath))
        {
            Directory.Delete(_folderPath, recursive: true);
        }
    }

    private ApplicationStore CreateStore()
    {
        return new ApplicationStore(new NullLogger<ApplicationStore>(), _fileSystem, _filePath);
    }

    [Test]
    public async Task SetThenFlush_RoundTripsThroughAFreshLoad()
    {
        var store = CreateStore();
        store.SetValue("Greeting", "hello");
        store.SetValue("Count", 7);
        await store.FlushAsync();

        // A second store over the same file reads the flushed values.
        var reloaded = CreateStore();
        reloaded.TryGetValue<string>("Greeting", out var greeting).Should().BeTrue();
        greeting.Should().Be("hello");
        reloaded.TryGetValue<int>("Count", out var count).Should().BeTrue();
        count.Should().Be(7);
    }

    [Test]
    public void SetValue_WithoutFlush_IsNotPersisted()
    {
        var store = CreateStore();
        store.SetValue("Count", 7);

        // No flush, so the in-memory change has not reached disk.
        var reloaded = CreateStore();
        reloaded.TryGetValue<int>("Count", out _).Should().BeFalse();
    }

    [Test]
    public async Task SetValue_AfterFlush_IsPersisted()
    {
        var store = CreateStore();
        store.SetValue("Count", 7);
        await store.FlushAsync();

        var reloaded = CreateStore();
        reloaded.TryGetValue<int>("Count", out var count).Should().BeTrue();
        count.Should().Be(7);
    }

    [Test]
    public async Task RemoveValue_AfterFlush_DeletesThePersistedValue()
    {
        var store = CreateStore();
        store.SetValue("Count", 7);
        await store.FlushAsync();

        store.RemoveValue("Count");
        await store.FlushAsync();

        var reloaded = CreateStore();
        reloaded.ContainsKey("Count").Should().BeFalse();
    }

    [Test]
    public void CorruptFile_LoadsAsEmptyStore()
    {
        File.WriteAllText(_filePath, "{ this is not valid json");

        var store = CreateStore();

        store.ContainsKey("Count").Should().BeFalse();
        store.TryGetValue<int>("Count", out _).Should().BeFalse();
    }

    [Test]
    public void MissingFile_LoadsAsEmptyStore()
    {
        var store = CreateStore();

        store.ContainsKey("Count").Should().BeFalse();
    }

    [Test]
    public void ResolveDefaultFilePath_ReturnsSettingsFileInPerUserFolder()
    {
        var path = ApplicationStore.ResolveDefaultFilePath();

        path.Should().NotBeNullOrWhiteSpace();
        Path.IsPathRooted(path).Should().BeTrue();
        Path.GetFileName(path).Should().Be("settings.json");
    }
}
