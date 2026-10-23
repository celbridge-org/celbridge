using Celbridge.Resources;
using Celbridge.Resources.Helpers;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Covers recording one operation's changes with the resolver at once, and reporting them once when the recorder is
/// disposed.
/// </summary>
[TestFixture]
public class ChangeRecorderTests
{
    private IResourceRootHandler _projectHandler = null!;
    private IResourceMonitor _resourceMonitor = null!;
    private IResourceService _resourceService = null!;

    [SetUp]
    public void Setup()
    {
        _projectHandler = Substitute.For<IResourceRootHandler>();
        var rootHandlers = Substitute.For<IRootHandlerRegistry>();
        rootHandlers.RootHandlers.Returns(new Dictionary<string, IResourceRootHandler>
        {
            [ResourceKey.DefaultRoot] = _projectHandler,
        });

        _resourceMonitor = Substitute.For<IResourceMonitor>();
        _resourceService = Substitute.For<IResourceService>();
        _resourceService.RootHandlers.Returns(rootHandlers);
        _resourceService.Monitor.Returns(_resourceMonitor);
    }

    [Test]
    public void ARecord_ReachesTheResolverBeforeTheReport()
    {
        using var changes = new ChangeRecorder(_resourceService);

        changes.RecordRemoved(new ResourceKey("old.txt"), "/project/old.txt");
        changes.RecordCreated(new ResourceKey("new.txt"), "/project/new.txt");

        _projectHandler.Received(1).RecordItemRemoved("/project/old.txt");
        _projectHandler.Received(1).RecordItemCreated("/project/new.txt");
        _resourceMonitor.DidNotReceiveWithAnyArgs().ReportOperationChanges(default!);
    }

    [Test]
    public void Disposing_ReportsEveryTouchedKeyOnce()
    {
        var changes = new ChangeRecorder(_resourceService);
        changes.RecordRemoved(new ResourceKey("old.txt"), "/project/old.txt");
        changes.RecordCreated(new ResourceKey("new.txt"), "/project/new.txt");
        changes.RecordChanged(new ResourceKey("locked.txt"));

        changes.Dispose();
        changes.Dispose();

        _resourceMonitor.Received(1).ReportOperationChanges(Arg.Is<IReadOnlyList<ResourceKey>>(resources =>
            resources.SequenceEqual(new[]
            {
                new ResourceKey("old.txt"),
                new ResourceKey("new.txt"),
                new ResourceKey("locked.txt"),
            })));
    }

    [Test]
    public void Disposing_WithNothingRecorded_ReportsNothing()
    {
        var changes = new ChangeRecorder(_resourceService);

        changes.Dispose();

        _resourceMonitor.DidNotReceiveWithAnyArgs().ReportOperationChanges(default!);
    }

    [Test]
    public void ARecordAfterTheReport_Throws()
    {
        var changes = new ChangeRecorder(_resourceService);
        changes.Dispose();

        var record = () => changes.RecordChanged(new ResourceKey("late.txt"));

        record.Should().Throw<InvalidOperationException>();
    }
}
