using Celbridge.Automation;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Resources.Services.Roots;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Helpers;
using Celbridge.Workspace;
using Microsoft.Extensions.Time.Testing;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for the subscriptions of ResourceMonitor: batching the touched keys, matching them against patterns and
/// checking the disk at delivery. The events come from a fake watcher, the files are real and the time moves only
/// when a test advances it.
/// </summary>
[TestFixture]
public class ResourceSubscriptionTests
{
    private const string CaseSensitiveVolumeReason = "On a case-sensitive volume, names that differ in case name different items.";

    // Records the batches of changes and the resyncs that a subscription receives. Each kind has a list, and
    // Deliveries keeps both kinds in the order they arrived.
    private sealed class Received
    {
        public List<ResourceChangeBatch> Batches { get; } = new();

        public List<ResourceResync> Resyncs { get; } = new();

        public List<object> Deliveries { get; } = new();

        public ResourceChangeHandlers Handlers => new(
            batch =>
            {
                Batches.Add(batch);
                Deliveries.Add(batch);
            },
            resync =>
            {
                Resyncs.Add(resync);
                Deliveries.Add(resync);
            });
    }

    private string _projectFolder = null!;
    private FakeTimeProvider _timeProvider = null!;
    private IAutomationService _automationService = null!;
    private ILocalFileSystem _fileSystem = null!;
    private Dictionary<string, IResourceRootHandler> _rootHandlers = null!;
    private IResourceService _resourceService = null!;
    private ResourceMonitor _resourceMonitor = null!;
    private FakeFileSystemMonitor _watcher = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolder = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(ResourceSubscriptionTests)}");
        if (Directory.Exists(_projectFolder))
        {
            Directory.Delete(_projectFolder, true);
        }
        Directory.CreateDirectory(_projectFolder);

        _timeProvider = new FakeTimeProvider();
        _automationService = Substitute.For<IAutomationService>();

        // Forward to the real file system, so a test can check which paths the disk check read.
        var localFileSystem = TestFileSystem.CreateLocal();
        _fileSystem = Substitute.For<ILocalFileSystem>();
        _fileSystem.GetInfoAsync(Arg.Any<string>())
            .Returns(callInfo => localFileSystem.GetInfoAsync(callInfo.Arg<string>()));

        // Run enqueued actions inline, so each batch reaches its subscriber while the time is advanced.
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.TryEnqueue(Arg.Any<Action>())
            .Returns(callInfo =>
            {
                callInfo.Arg<Action>().Invoke();
                return true;
            });

        _rootHandlers = new Dictionary<string, IResourceRootHandler>
        {
            [ResourceKey.DefaultRoot] = new ProjectRootHandler(_projectFolder),
        };
        var rootHandlerRegistry = Substitute.For<IRootHandlerRegistry>();
        rootHandlerRegistry.RootHandlers.Returns(_rootHandlers);

        _resourceService = Substitute.For<IResourceService>();
        _resourceService.RootHandlers.Returns(rootHandlerRegistry);
        _resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(_resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var monitorFactory = new FakeFileSystemMonitorFactory();

        _resourceMonitor = new ResourceMonitor(
            Substitute.For<ILogger<ResourceMonitor>>(),
            dispatcher,
            workspaceWrapper,
            monitorFactory,
            _fileSystem,
            _automationService,
            _timeProvider);

        _resourceMonitor.Initialize().IsSuccess.Should().BeTrue();
        _watcher = monitorFactory.Created.Single();
    }

    [TearDown]
    public void TearDown()
    {
        _resourceMonitor.Shutdown();

        if (Directory.Exists(_projectFolder))
        {
            Directory.Delete(_projectFolder, true);
        }
    }

    [Test]
    public void ABatchIsDeliveredOneIntervalAfterItsFirstEvent()
    {
        var batches = Subscribe("**");
        WriteProjectFile("notes.txt");

        _watcher.RaiseChanged(PathFor("notes.txt"));
        _timeProvider.Advance(TimeSpan.FromMilliseconds(60));
        _watcher.RaiseChanged(PathFor("other.txt"));
        _timeProvider.Advance(TimeSpan.FromMilliseconds(39));
        batches.Should().BeEmpty();

        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));

        batches.Should().ContainSingle();
        batches[0].RootName.Should().Be(ResourceKey.DefaultRoot);
        batches[0].Changes.Should().Equal(
            new ResourceChange(ResourceKey.Create("notes.txt"), ResourceItemState.File),
            new ResourceChange(ResourceKey.Create("other.txt"), ResourceItemState.Absent));
    }

    [Test]
    public void EachDeliveredBatchIsReported()
    {
        Subscribe("**");
        WriteProjectFile("notes.txt");

        _watcher.RaiseChanged(PathFor("notes.txt"));
        _watcher.RaiseChanged(PathFor("other.txt"));
        AdvanceOneInterval();

        _automationService.Received(1).LogEvent(Arg.Is<ResourceChangesDeliveredEvent>(
            delivered => delivered.RootName == ResourceKey.DefaultRoot && delivered.Keys == 2));
    }

    [Test]
    public void AKeyTouchedSeveralTimesAppearsOnceInABatch()
    {
        var batches = Subscribe("**");
        WriteProjectFile("notes.txt");

        _watcher.RaiseCreated(PathFor("notes.txt"));
        _watcher.RaiseChanged(PathFor("notes.txt"));
        _watcher.RaiseRenamed(PathFor("notes.txt.tmp"), PathFor("notes.txt"));
        AdvanceOneInterval();

        batches.Should().ContainSingle();
        batches[0].Changes.Should().Equal(
            new ResourceChange(ResourceKey.Create("notes.txt"), ResourceItemState.File),
            new ResourceChange(ResourceKey.Create("notes.txt.tmp"), ResourceItemState.Absent));
    }

    [Test]
    public void AKeyTouchedDuringADeliveryArrivesInTheNextBatch()
    {
        var batches = new List<ResourceChangeBatch>();
        var handlers = new ResourceChangeHandlers(
            batch =>
            {
                batches.Add(batch);
                if (batches.Count == 1)
                {
                    _watcher.RaiseChanged(PathFor("second.txt"));
                }
            },
            _ => { });
        Subscribe("**", handlers);

        _watcher.RaiseChanged(PathFor("first.txt"));
        AdvanceOneInterval();
        batches.Should().ContainSingle();

        AdvanceOneInterval();

        batches.Should().HaveCount(2);
        batches[1].Changes.Should().Equal(new ResourceChange(ResourceKey.Create("second.txt"), ResourceItemState.Absent));
    }

    [Test]
    public void OnlyTheKeysThatMatchAPatternAreCheckedAndDelivered()
    {
        var batches = Subscribe("docs/*.md");

        _watcher.RaiseChanged(PathFor("docs/guide.md"));
        _watcher.RaiseChanged(PathFor("docs/data.json"));
        _watcher.RaiseChanged(PathFor("docs/sub/deep.md"));
        _watcher.RaiseChanged(PathFor("guide.md"));
        AdvanceOneInterval();

        batches.Should().ContainSingle();
        batches[0].Changes.Should().Equal(new ResourceChange(ResourceKey.Create("docs/guide.md"), ResourceItemState.Absent));
        _fileSystem.Received(1).GetInfoAsync(PathFor("docs/guide.md"));
        _fileSystem.Received(1).GetInfoAsync(Arg.Any<string>());
    }

    [Test]
    public void APatternWithoutASlashNamesOneTopLevelItem()
    {
        var batches = Subscribe("readme.md");

        _watcher.RaiseChanged(PathFor("docs/readme.md"));
        _watcher.RaiseChanged(PathFor("readme.md"));
        AdvanceOneInterval();

        batches.Should().ContainSingle();
        batches[0].Changes.Should().Equal(new ResourceChange(ResourceKey.Create("readme.md"), ResourceItemState.Absent));
    }

    [Test]
    public void EachSubscriptionReceivesOnlyItsOwnMatches()
    {
        var markdownBatches = Subscribe("**/*.md");
        var jsonBatches = Subscribe("**/*.json");

        // The files exist, so neither can be a folder with a match inside it.
        WriteProjectFile("a.md");
        WriteProjectFile("b.json");
        _watcher.RaiseChanged(PathFor("a.md"));
        _watcher.RaiseChanged(PathFor("b.json"));
        AdvanceOneInterval();

        markdownBatches.Single().Changes.Should().Equal(new ResourceChange(ResourceKey.Create("a.md"), ResourceItemState.File));
        jsonBatches.Single().Changes.Should().Equal(new ResourceChange(ResourceKey.Create("b.json"), ResourceItemState.File));
    }

    [Test]
    public void AKeyInTheGitFolderIsDropped()
    {
        var batches = Subscribe("**");

        _watcher.RaiseChanged(PathFor(".git/index"));
        AdvanceOneInterval();

        batches.Should().BeEmpty();
    }

    [Test]
    public void AKeyThatTheSearchListExcludesIsDelivered()
    {
        var policy = TestResourcePolicy.CreateWithSearchExclude(_projectFolder, ["build"]);
        _resourceService.Policy.Returns(policy);
        var batches = Subscribe("build/**");

        _watcher.RaiseChanged(PathFor("build/output.txt"));
        AdvanceOneInterval();

        var expected = new ResourceChange(ResourceKey.Create("build/output.txt"), ResourceItemState.Absent);
        batches.Single().Changes.Should().Equal(expected);
    }

    [Test]
    public void ALinkIsReportedAsAbsent()
    {
        var batches = Subscribe("**");
        var outsideFile = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(ResourceSubscriptionTests)}_target.txt");
        File.WriteAllText(outsideFile, "outside");
        try
        {
            File.CreateSymbolicLink(PathFor("link.txt"), outsideFile);
        }
        catch (Exception)
        {
            Assert.Ignore("Cannot create symbolic links on this system");
        }

        _watcher.RaiseCreated(PathFor("link.txt"));
        AdvanceOneInterval();

        batches.Single().Changes.Should().Equal(new ResourceChange(ResourceKey.Create("link.txt"), ResourceItemState.Absent));
        File.Delete(outsideFile);
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = CaseSensitiveVolumeReason)]
    public void TheOldNameOfACaseOnlyRenameIsReportedAsAbsent()
    {
        var batches = Subscribe("**");
        WriteProjectFile("notes.txt");

        // Resolving the key lists the folder, so the listing has the old name.
        ResolveKey("notes.txt").IsSuccess.Should().BeTrue();

        // On macOS the watcher reports a create for both names.
        File.Move(PathFor("notes.txt"), PathFor("Notes.txt"));
        _watcher.RaiseCreated(PathFor("notes.txt"));
        _watcher.RaiseCreated(PathFor("Notes.txt"));
        AdvanceOneInterval();

        batches.Single().Changes.Should().Equal(
            new ResourceChange(ResourceKey.Create("notes.txt"), ResourceItemState.Absent),
            new ResourceChange(ResourceKey.Create("Notes.txt"), ResourceItemState.File));
    }

    [Test]
    public void AChangeToAFolderReachesAPatternForAPathInsideIt()
    {
        var batches = Subscribe("docs/guide.md");
        WriteProjectFile("docs/guide.md");

        // The watcher reports a renamed folder alone, with nothing for the items inside it.
        Directory.Move(PathFor("docs"), PathFor("archive"));
        _watcher.RaiseRenamed(PathFor("docs"), PathFor("archive"));
        AdvanceOneInterval();

        batches.Single().Changes.Should().Equal(new ResourceChange(ResourceKey.Create("docs"), ResourceItemState.Absent));
    }

    [Test]
    public void AnExistingFolderArrivesAsAFolder()
    {
        var batches = Subscribe("docs/guide.md");
        Directory.CreateDirectory(PathFor("docs"));

        _watcher.RaiseCreated(PathFor("docs"));
        AdvanceOneInterval();

        batches.Single().Changes.Should().Equal(new ResourceChange(ResourceKey.Create("docs"), ResourceItemState.Folder));
    }

    [Test]
    public void AnExistingFileIsNotTreatedAsAFolder()
    {
        var batches = Subscribe("docs/guide.md");
        WriteProjectFile("docs");

        _watcher.RaiseChanged(PathFor("docs"));
        AdvanceOneInterval();

        batches.Should().BeEmpty();
    }

    [Test]
    public void AFolderOnlyPatternMatchesAnAbsentKeyAsAFolder()
    {
        var batches = Subscribe("build/");

        _watcher.RaiseDeleted(PathFor("build"));
        AdvanceOneInterval();

        batches.Single().Changes.Should().Equal(new ResourceChange(ResourceKey.Create("build"), ResourceItemState.Absent));
    }

    [Test]
    public void AWatcherErrorSendsAResyncAtOnce()
    {
        var received = new Received();
        Subscribe("docs/*.md", received.Handlers);

        _watcher.RaiseDesynchronized();
        _timeProvider.Advance(TimeSpan.Zero);

        received.Batches.Should().BeEmpty();
        received.Resyncs.Should().Equal(new ResourceResync(ResourceKey.DefaultRoot));
    }

    [Test]
    public void PendingChangesArriveBeforeTheResync()
    {
        var received = new Received();
        Subscribe("**", received.Handlers);

        _watcher.RaiseChanged(PathFor("notes.txt"));
        _watcher.RaiseDesynchronized();
        _timeProvider.Advance(TimeSpan.Zero);

        received.Deliveries.Should().HaveCount(2);
        var batch = received.Deliveries[0].Should().BeOfType<ResourceChangeBatch>().Subject;
        batch.Changes.Should().Equal(new ResourceChange(ResourceKey.Create("notes.txt"), ResourceItemState.Absent));
        received.Deliveries[1].Should().Be(new ResourceResync(ResourceKey.DefaultRoot));
    }

    [Test]
    public void SubscribingAgainBeforeDisposingMissesNoChange()
    {
        var oldBatches = new List<ResourceChangeBatch>();
        var newBatches = new List<ResourceChangeBatch>();
        var oldSubscription = _resourceMonitor
            .SubscribeToMatcher(ResourceKey.DefaultRoot, Anchored("old.txt"), ChangesOnly(oldBatches))
            .Value;

        // The touch arrives while only the old subscription exists. The batch is matched when it closes.
        _watcher.RaiseChanged(PathFor("new.txt"));
        _resourceMonitor.SubscribeToMatcher(ResourceKey.DefaultRoot, Anchored("new.txt"), ChangesOnly(newBatches));
        oldSubscription.Dispose();
        AdvanceOneInterval();

        oldBatches.Should().BeEmpty();
        newBatches.Single().Changes.Should().Equal(new ResourceChange(ResourceKey.Create("new.txt"), ResourceItemState.Absent));
    }

    [Test]
    public void AMatcherInTheProjectSyntaxMatchesANameAtAnyDepth()
    {
        // A console trigger compiles its pattern in the project's syntax, where a name without a slash matches at any
        // depth.
        var batches = new List<ResourceChangeBatch>();
        var matcher = ResourcePathMatcher.Compile("*.py");
        _resourceMonitor.SubscribeToMatcher(ResourceKey.DefaultRoot, matcher, ChangesOnly(batches));
        WriteProjectFile("scripts/tool.py");
        WriteProjectFile("notes.txt");

        _watcher.RaiseChanged(PathFor("scripts/tool.py"));
        _watcher.RaiseChanged(PathFor("notes.txt"));
        AdvanceOneInterval();

        var expected = new ResourceChange(ResourceKey.Create("scripts/tool.py"), ResourceItemState.File);
        batches.Single().Changes.Should().Equal(expected);
    }

    [Test]
    public void SubscribingToAnUnknownRootFails()
    {
        var subscribeResult = _resourceMonitor.SubscribeToMatcher("missing", Anchored("**"), new Received().Handlers);

        subscribeResult.IsFailure.Should().BeTrue();
    }

    [Test]
    public void ADisposedSubscriptionReceivesNothing()
    {
        var received = new Received();
        var matcher = Anchored("**");
        var handlers = received.Handlers;
        var subscription = _resourceMonitor.SubscribeToMatcher(ResourceKey.DefaultRoot, matcher, handlers).Value;

        subscription.Dispose();
        _watcher.RaiseChanged(PathFor("notes.txt"));
        _watcher.RaiseDesynchronized();
        AdvanceOneInterval();

        received.Deliveries.Should().BeEmpty();
    }

    [Test]
    public void AKeySubscriptionReceivesItsKeyWhenTheKeyIsTouched()
    {
        var batches = SubscribeToKeys("docs/guide.md", "docs/other.md");
        WriteProjectFile("docs/guide.md");

        _watcher.RaiseChanged(PathFor("docs/guide.md"));
        _watcher.RaiseChanged(PathFor("docs/sibling.md"));
        AdvanceOneInterval();

        batches.Single().Changes.Should().Equal(new ResourceChange(ResourceKey.Create("docs/guide.md"), ResourceItemState.File));
    }

    [Test]
    public void AKeySubscriptionReceivesItsKeyWhenAFolderAboveItIsTouched()
    {
        var batches = SubscribeToKeys("docs/sub/guide.md");
        WriteProjectFile("docs/sub/guide.md");

        // The watcher reports a renamed folder alone, with nothing for the items inside it.
        Directory.Move(PathFor("docs"), PathFor("archive"));
        _watcher.RaiseRenamed(PathFor("docs"), PathFor("archive"));
        AdvanceOneInterval();

        var expected = new ResourceChange(ResourceKey.Create("docs/sub/guide.md"), ResourceItemState.Absent);
        batches.Single().Changes.Should().Equal(expected);
    }

    [Test]
    public void AKeySubscriptionReceivesNothingForATouchBelowItsKey()
    {
        var batches = SubscribeToKeys("docs");

        _watcher.RaiseChanged(PathFor("docs/guide.md"));
        AdvanceOneInterval();

        batches.Should().BeEmpty();
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = CaseSensitiveVolumeReason)]
    public void AKeySubscriptionReceivesItsOwnKeyForATouchInAnotherCase()
    {
        var batches = SubscribeToKeys("notes.txt");
        WriteProjectFile("notes.txt");

        // On macOS the watcher reports a delete in the letter case of the call that made it.
        _watcher.RaiseChanged(PathFor("NOTES.TXT"));
        AdvanceOneInterval();
        File.Delete(PathFor("NOTES.TXT"));
        _watcher.RaiseDeleted(PathFor("NOTES.TXT"));
        AdvanceOneInterval();

        batches.Should().HaveCount(2);
        batches[0].Changes.Should().Equal(new ResourceChange(ResourceKey.Create("notes.txt"), ResourceItemState.File));
        batches[1].Changes.Should().Equal(new ResourceChange(ResourceKey.Create("notes.txt"), ResourceItemState.Absent));
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = CaseSensitiveVolumeReason)]
    public void AKeyInAnotherCaseThanTheStoredNameIsReportedAsAbsent()
    {
        var batches = SubscribeToKeys("notes.txt");
        WriteProjectFile("notes.txt");

        File.Move(PathFor("notes.txt"), PathFor("Notes.txt"));
        _watcher.RaiseCreated(PathFor("notes.txt"));
        _watcher.RaiseCreated(PathFor("Notes.txt"));
        AdvanceOneInterval();

        batches.Single().Changes.Should().Equal(new ResourceChange(ResourceKey.Create("notes.txt"), ResourceItemState.Absent));
    }

    [Test]
    public void AKeyReachedTwiceIsCheckedOnce()
    {
        var batches = SubscribeToKeys("docs/guide.md");
        var patternBatches = Subscribe("docs/guide.md");

        _watcher.RaiseDeleted(PathFor("docs"));
        _watcher.RaiseDeleted(PathFor("docs/guide.md"));
        AdvanceOneInterval();

        batches.Single().Changes.Should().Equal(new ResourceChange(ResourceKey.Create("docs/guide.md"), ResourceItemState.Absent));
        patternBatches.Single().Changes.Should().HaveCount(2);
        _fileSystem.Received(1).GetInfoAsync(PathFor("docs/guide.md"));
    }

    [Test]
    public void AKeySubscriptionReceivesAResync()
    {
        var received = new Received();
        var resources = new List<ResourceKey>
        {
            ResourceKey.Create("notes.txt"),
        };
        _resourceMonitor.SubscribeToResources(resources, received.Handlers).IsSuccess.Should().BeTrue();

        _watcher.RaiseDesynchronized();
        _timeProvider.Advance(TimeSpan.Zero);

        received.Batches.Should().BeEmpty();
        received.Resyncs.Should().Equal(new ResourceResync(ResourceKey.DefaultRoot));
    }

    [Test]
    public void SubscribingToKeysAgainBeforeDisposingMissesNoChange()
    {
        var oldBatches = new List<ResourceChangeBatch>();
        var newBatches = new List<ResourceChangeBatch>();
        var newKey = ResourceKey.Create("new.txt");
        var oldSubscription = _resourceMonitor
            .SubscribeToResources([ResourceKey.Create("old.txt")], ChangesOnly(oldBatches))
            .Value;

        _watcher.RaiseChanged(PathFor("new.txt"));
        _resourceMonitor.SubscribeToResources([newKey], ChangesOnly(newBatches));
        oldSubscription.Dispose();
        AdvanceOneInterval();

        oldBatches.Should().BeEmpty();
        newBatches.Single().Changes.Should().Equal(new ResourceChange(newKey, ResourceItemState.Absent));
    }

    [Test]
    public void ARootKeyOrAKeyInAnUnknownRootIsRefused()
    {
        var rootResult = _resourceMonitor.SubscribeToResources([ResourceKey.Empty], new Received().Handlers);
        var unknownRootKey = ResourceKey.Create("missing:notes.txt");
        var unknownRootResult = _resourceMonitor.SubscribeToResources([unknownRootKey], new Received().Handlers);

        rootResult.IsFailure.Should().BeTrue();
        unknownRootResult.IsFailure.Should().BeTrue();
    }

    [Test]
    public void ADisposedKeySubscriptionReceivesNothing()
    {
        var received = new Received();
        var resources = new List<ResourceKey>
        {
            ResourceKey.Create("notes.txt"),
        };
        var subscription = _resourceMonitor.SubscribeToResources(resources, received.Handlers).Value;

        subscription.Dispose();
        _watcher.RaiseChanged(PathFor("notes.txt"));
        _watcher.RaiseDesynchronized();
        AdvanceOneInterval();

        received.Deliveries.Should().BeEmpty();
    }

    private List<ResourceChangeBatch> SubscribeToKeys(params string[] keyPaths)
    {
        var batches = new List<ResourceChangeBatch>();
        var resources = keyPaths.Select(ResourceKey.Create).ToList();
        var subscribeResult = _resourceMonitor.SubscribeToResources(resources, ChangesOnly(batches));
        subscribeResult.IsSuccess.Should().BeTrue(subscribeResult.FirstErrorMessage);

        return batches;
    }

    private List<ResourceChangeBatch> Subscribe(string pattern)
    {
        var batches = new List<ResourceChangeBatch>();
        Subscribe(pattern, ChangesOnly(batches));

        return batches;
    }

    private void Subscribe(string pattern, ResourceChangeHandlers handlers)
    {
        var matcher = Anchored(pattern);
        var subscribeResult = _resourceMonitor.SubscribeToMatcher(ResourceKey.DefaultRoot, matcher, handlers);
        subscribeResult.IsSuccess.Should().BeTrue(subscribeResult.FirstErrorMessage);
    }

    // Records the batches and drops the resyncs.
    private static ResourceChangeHandlers ChangesOnly(List<ResourceChangeBatch> batches)
    {
        return new ResourceChangeHandlers(batches.Add, _ => { });
    }

    private static IPathMatcher Anchored(string pattern)
    {
        return ResourcePathMatcher.Compile(pattern, anchored: true);
    }

    private void AdvanceOneInterval()
    {
        _timeProvider.Advance(ChangeBatcher.BatchInterval);
    }

    private Result<string> ResolveKey(string keyPath)
    {
        return _rootHandlers[ResourceKey.DefaultRoot].Resolve(ResourceKey.Create(keyPath));
    }

    private string PathFor(string keyPath)
    {
        return Path.GetFullPath(Path.Combine(_projectFolder, keyPath));
    }

    // Writes a file at the key's path in the project, creating its folders.
    private void WriteProjectFile(string keyPath)
    {
        var filePath = PathFor(keyPath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, "content");
    }
}
