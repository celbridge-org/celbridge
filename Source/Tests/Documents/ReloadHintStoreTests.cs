namespace Celbridge.Tests.Documents;

/// <summary>
/// Tests for ReloadHintStore — register/consume round-trip, consume-removes,
/// overwrite semantics, and TTL expiry. A controllable clock keeps the TTL
/// tests deterministic.
/// </summary>
[TestFixture]
public class ReloadHintStoreTests
{
    private DateTime _nowUtc;
    private ReloadHintStore _store = null!;

    [SetUp]
    public void Setup()
    {
        _nowUtc = new DateTime(2026, 5, 29, 12, 0, 0, DateTimeKind.Utc);
        _store = new ReloadHintStore(TimeSpan.FromSeconds(2), () => _nowUtc);
    }

    [Test]
    public void Consume_ReturnsPreserveViewState_WhenNoHintRegistered()
    {
        var resource = new ResourceKey("doc.xlsx");

        var hint = _store.Consume(resource);

        hint.Should().Be(ReloadHint.PreserveViewState);
    }

    [Test]
    public void Register_ThenConsume_ReturnsRegisteredHint()
    {
        var resource = new ResourceKey("doc.xlsx");
        _store.Register(resource, ReloadHint.DiskWinsOnViewState);

        var hint = _store.Consume(resource);

        hint.Should().Be(ReloadHint.DiskWinsOnViewState);
    }

    [Test]
    public void Consume_RemovesTheEntry_SoSecondConsumeReturnsDefault()
    {
        var resource = new ResourceKey("doc.xlsx");
        _store.Register(resource, ReloadHint.DiskWinsOnViewState);

        _store.Consume(resource);
        var secondHint = _store.Consume(resource);

        secondHint.Should().Be(ReloadHint.PreserveViewState);
    }

    [Test]
    public void Register_OverwritesPriorHintForTheSameResource()
    {
        var resource = new ResourceKey("doc.xlsx");
        _store.Register(resource, ReloadHint.PreserveViewState);
        _store.Register(resource, ReloadHint.DiskWinsOnViewState);

        var hint = _store.Consume(resource);

        hint.Should().Be(ReloadHint.DiskWinsOnViewState);
    }

    [Test]
    public void Register_KeepsHintsForDifferentResourcesIndependent()
    {
        var resourceA = new ResourceKey("a.xlsx");
        var resourceB = new ResourceKey("b.xlsx");
        _store.Register(resourceA, ReloadHint.DiskWinsOnViewState);
        _store.Register(resourceB, ReloadHint.PreserveViewState);

        _store.Consume(resourceA).Should().Be(ReloadHint.DiskWinsOnViewState);
        _store.Consume(resourceB).Should().Be(ReloadHint.PreserveViewState);
    }

    [Test]
    public void Consume_ReturnsDefault_WhenHintIsPastTtl()
    {
        var resource = new ResourceKey("doc.xlsx");
        _store.Register(resource, ReloadHint.DiskWinsOnViewState);

        _nowUtc = _nowUtc.AddSeconds(3);

        var hint = _store.Consume(resource);

        hint.Should().Be(ReloadHint.PreserveViewState);
    }

    [Test]
    public void Consume_ReturnsHint_WhenStillWithinTtl()
    {
        var resource = new ResourceKey("doc.xlsx");
        _store.Register(resource, ReloadHint.DiskWinsOnViewState);

        _nowUtc = _nowUtc.AddSeconds(1);

        var hint = _store.Consume(resource);

        hint.Should().Be(ReloadHint.DiskWinsOnViewState);
    }

    [Test]
    public void Consume_RemovesExpiredEntry_SoFollowupConsumeIsCheap()
    {
        var resource = new ResourceKey("doc.xlsx");
        _store.Register(resource, ReloadHint.DiskWinsOnViewState);

        _nowUtc = _nowUtc.AddSeconds(3);
        _store.Consume(resource);

        // The expired entry was removed by the first Consume call. Register a
        // fresh hint and verify it is honoured without leakage from the prior
        // expired entry.
        _nowUtc = _nowUtc.AddSeconds(1);
        _store.Register(resource, ReloadHint.PreserveViewState);

        _store.Consume(resource).Should().Be(ReloadHint.PreserveViewState);
    }
}
