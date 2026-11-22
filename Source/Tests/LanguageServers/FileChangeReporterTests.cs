using System.Threading.Channels;
using Celbridge.LanguageServers.Helpers;
using Celbridge.LanguageServers.Services;
using Celbridge.Resources;
using Celbridge.Tests.Helpers;

namespace Celbridge.Tests.LanguageServers;

/// <summary>
/// Covers the reporter's file events for each kind of change in the project. The kinds are a new file, a known
/// file, an absent path, a new folder, a known folder and lost events.
/// </summary>
[TestFixture]
public class FileChangeReporterTests
{
    private const int CreatedType = 1;
    private const int ChangedType = 2;
    private const int DeletedType = 3;

    private static readonly string ProjectFolderPath = OperatingSystem.IsWindows()
        ? @"C:\Projects\Acme"
        : "/projects/acme";

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    private FakeResourceMonitor _monitor = null!;
    private IResourceWalker _walker = null!;
    private FakeReceiver _receiver = null!;
    private FileChangeReporter _reporter = null!;

    [SetUp]
    public void Setup()
    {
        _monitor = new FakeResourceMonitor();
        _receiver = new FakeReceiver();

        // The project folder is empty unless a test sets its contents.
        _walker = Substitute.For<IResourceWalker>();
        SetFolderContents(ResourceKey.Empty);

        var reportedFiles = new ReportedFiles(
            new HashSet<string> { ".py", ".pyi" },
            new HashSet<string> { "pyrightconfig.json" });

        _reporter = new FileChangeReporter(
            _monitor,
            _walker,
            reportedFiles,
            ProjectFolderPath,
            _receiver,
            new NullLogger<FileChangeReporter>());
    }

    [TearDown]
    public void TearDown()
    {
        _reporter.Dispose();
    }

    [Test]
    public async Task ANewFile_IsReportedAsCreated()
    {
        _reporter.Start().IsSuccess.Should().BeTrue();

        _monitor.DeliverChange("src/helper.py", ResourceItemState.File);
        var fileEvents = await _receiver.ReadAsync();

        fileEvents.Should().Equal(new FileEvent(UriOf("src", "helper.py"), CreatedType));
    }

    [Test]
    public async Task AFileListedAtTheStart_IsReportedAsChanged()
    {
        SetFolderContents(ResourceKey.Empty, Walked("src", isFolder: true), Walked("src/helper.py", isFolder: false));
        _reporter.Start().IsSuccess.Should().BeTrue();

        _monitor.DeliverChange("src/helper.py", ResourceItemState.File);
        var fileEvents = await _receiver.ReadAsync();

        fileEvents.Should().Equal(new FileEvent(UriOf("src", "helper.py"), ChangedType));
    }

    [Test]
    public async Task AFileReportedBefore_IsReportedAsChanged()
    {
        _reporter.Start().IsSuccess.Should().BeTrue();

        _monitor.DeliverChange("main.py", ResourceItemState.File);
        await _receiver.ReadAsync();
        _monitor.DeliverChange("main.py", ResourceItemState.File);
        var fileEvents = await _receiver.ReadAsync();

        fileEvents.Should().Equal(new FileEvent(UriOf("main.py"), ChangedType));
    }

    [Test]
    public async Task AFileNamedByTheDefinition_IsReported()
    {
        _reporter.Start().IsSuccess.Should().BeTrue();

        _monitor.DeliverChange("PyrightConfig.json", ResourceItemState.File);
        var fileEvents = await _receiver.ReadAsync();

        fileEvents.Should().Equal(new FileEvent(UriOf("PyrightConfig.json"), CreatedType));
    }

    [Test]
    public async Task AFileOfAnotherKind_IsNotReported()
    {
        _reporter.Start().IsSuccess.Should().BeTrue();

        _monitor.DeliverChange("notes.txt", ResourceItemState.File);
        _monitor.DeliverChange("main.py", ResourceItemState.File);
        var fileEvents = await _receiver.ReadAsync();

        fileEvents.Should().Equal(new FileEvent(UriOf("main.py"), CreatedType));
    }

    [Test]
    public async Task AnAbsentPath_IsReportedAsDeleted()
    {
        _reporter.Start().IsSuccess.Should().BeTrue();

        _monitor.DeliverChange("src/package", ResourceItemState.Absent);
        var fileEvents = await _receiver.ReadAsync();

        fileEvents.Should().Equal(new FileEvent(UriOf("src", "package"), DeletedType));
    }

    [Test]
    public async Task AFileInsideADeletedFolder_IsNewWhenItReturns()
    {
        SetFolderContents(ResourceKey.Empty, Walked("src", isFolder: true), Walked("src/helper.py", isFolder: false));
        _reporter.Start().IsSuccess.Should().BeTrue();

        _monitor.DeliverChange("src", ResourceItemState.Absent);
        await _receiver.ReadAsync();
        _monitor.DeliverChange("src/helper.py", ResourceItemState.File);
        var fileEvents = await _receiver.ReadAsync();

        fileEvents.Should().Equal(new FileEvent(UriOf("src", "helper.py"), CreatedType));
    }

    [Test]
    public async Task ANewFolder_ReportsEachMatchingFileInsideAsCreated()
    {
        SetFolderContents(
            new ResourceKey("package"),
            Walked("package/sub", isFolder: true),
            Walked("package/__init__.py", isFolder: false),
            Walked("package/sub/module.pyi", isFolder: false),
            Walked("package/readme.md", isFolder: false));
        _reporter.Start().IsSuccess.Should().BeTrue();

        _monitor.DeliverChange("package", ResourceItemState.Folder);
        var fileEvents = await _receiver.ReadAsync();

        fileEvents.Should().Equal(
            new FileEvent(UriOf("package", "__init__.py"), CreatedType),
            new FileEvent(UriOf("package", "sub", "module.pyi"), CreatedType));
    }

    [Test]
    public async Task AKnownFolder_ReportsNothing()
    {
        SetFolderContents(ResourceKey.Empty, Walked("src", isFolder: true), Walked("src/helper.py", isFolder: false));
        _reporter.Start().IsSuccess.Should().BeTrue();

        // The project folder and a listed folder each change. Only the file's own change makes an event.
        _monitor.DeliverChange(ResourceKey.Empty, ResourceItemState.Folder);
        _monitor.DeliverChange("src", ResourceItemState.Folder);
        _monitor.DeliverChange("src/new.py", ResourceItemState.File);
        var fileEvents = await _receiver.ReadAsync();

        fileEvents.Should().Equal(new FileEvent(UriOf("src", "new.py"), CreatedType));
        await _walker.DidNotReceive().WalkAsync(new ResourceKey("src"), Arg.Any<bool>(), Arg.Any<IPathMatcher?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LostEvents_ReportTheProjectFolderAsChanged()
    {
        _reporter.Start().IsSuccess.Should().BeTrue();

        _monitor.DeliverResync(ResourceKey.DefaultRoot);
        var fileEvents = await _receiver.ReadAsync();

        fileEvents.Should().Equal(new FileEvent(LspUri.FromPath(ProjectFolderPath), ChangedType));
    }

    [Test]
    public async Task LostEvents_ListTheProjectAgain()
    {
        _reporter.Start().IsSuccess.Should().BeTrue();
        _monitor.DeliverChange("old.py", ResourceItemState.File);
        await _receiver.ReadAsync();

        // The listing after the lost events finds only the added file.
        SetFolderContents(ResourceKey.Empty, Walked("added.py", isFolder: false));
        _monitor.DeliverResync(ResourceKey.DefaultRoot);
        await _receiver.ReadAsync();
        _monitor.DeliverChange("added.py", ResourceItemState.File);
        var addedEvents = await _receiver.ReadAsync();
        _monitor.DeliverChange("old.py", ResourceItemState.File);
        var oldEvents = await _receiver.ReadAsync();

        addedEvents.Should().Equal(new FileEvent(UriOf("added.py"), ChangedType));
        oldEvents.Should().Equal(new FileEvent(UriOf("old.py"), CreatedType));
    }

    [Test]
    public void Start_ARootThatIsNotWatched_Fails()
    {
        _monitor.RefusedMatcherRoots.Add(ResourceKey.DefaultRoot);

        var result = _reporter.Start();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void Dispose_EndsTheSubscription()
    {
        _reporter.Start().IsSuccess.Should().BeTrue();

        _reporter.Dispose();

        _monitor.MatcherSubscriptions.Should().BeEmpty();
    }

    private void SetFolderContents(ResourceKey folder, params WalkedItem[] items)
    {
        _walker.WalkAsync(folder, Arg.Any<bool>(), Arg.Any<IPathMatcher?>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<WalkedItem>>.Ok(items));
    }

    private static string UriOf(params string[] names)
    {
        var path = ProjectFolderPath;
        foreach (var name in names)
        {
            path = Path.Combine(path, name);
        }

        return LspUri.FromPath(path);
    }

    private static WalkedItem Walked(string keyPath, bool isFolder)
    {
        var path = Path.Combine(ProjectFolderPath, keyPath.Replace('/', Path.DirectorySeparatorChar));

        return new WalkedItem(new ResourceKey(keyPath), path, isFolder, 0, DateTime.UnixEpoch, default);
    }

    private sealed class FakeReceiver : IFileChangeReceiver
    {
        private readonly Channel<IReadOnlyList<FileEvent>> _reports = Channel.CreateUnbounded<IReadOnlyList<FileEvent>>();

        public void ReportFileChanges(IReadOnlyList<FileEvent> fileEvents)
        {
            _reports.Writer.TryWrite(fileEvents);
        }

        public async Task<IReadOnlyList<FileEvent>> ReadAsync()
        {
            return await _reports.Reader.ReadAsync().AsTask().WaitAsync(ReadTimeout);
        }
    }
}
