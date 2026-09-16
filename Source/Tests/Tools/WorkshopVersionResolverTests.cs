using Celbridge.Tools;
using Celbridge.Workshop;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for WorkshopVersionResolver, the workshop version or alias selection shared by
/// workshop_install_package and workshop_delete_package. 'latest' selects the highest live
/// workshop version, and a number or alias dereferences to its target regardless of deletion,
/// leaving the download or delete to surface a deleted target.
/// </summary>
[TestFixture]
public class WorkshopVersionResolverTests
{
    private static RemoteWorkshopVersion WorkshopVersion(int workshopVersion, bool deleted = false)
    {
        var date = new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc);
        return new RemoteWorkshopVersion(workshopVersion, "Acme", date, deleted, "sha256:abc", "Summary.");
    }

    private static RemotePackageDetails Details(
        IReadOnlyList<RemoteWorkshopVersion> workshopVersions,
        IReadOnlyList<RemotePackageAlias>? aliases = null)
    {
        var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return new RemotePackageDetails("my-widget", createdAt, workshopVersions, aliases ?? Array.Empty<RemotePackageAlias>());
    }

    [Test]
    public void Resolve_Latest_SelectsHighestNonDeletedWorkshopVersion()
    {
        var details = Details(new List<RemoteWorkshopVersion>
        {
            WorkshopVersion(1),
            WorkshopVersion(2),
            WorkshopVersion(3, deleted: true),
        });

        var result = WorkshopVersionResolver.Resolve(details, "latest");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
    }

    [Test]
    public void Resolve_LatestWithAllWorkshopVersionsDeleted_Fails()
    {
        var details = Details(new List<RemoteWorkshopVersion>
        {
            WorkshopVersion(1, deleted: true),
            WorkshopVersion(2, deleted: true),
        });

        var result = WorkshopVersionResolver.Resolve(details, "latest");

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("no live workshop version");
    }

    [Test]
    public void Resolve_ExplicitDeletedWorkshopVersion_ResolvesToTarget()
    {
        // Resolution does not consider liveness. The download surfaces the deletion.
        var details = Details(new List<RemoteWorkshopVersion>
        {
            WorkshopVersion(1, deleted: true),
            WorkshopVersion(2),
        });

        var result = WorkshopVersionResolver.Resolve(details, "1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }

    [Test]
    public void Resolve_AliasToDeletedWorkshopVersion_ResolvesToTarget()
    {
        var details = Details(
            new List<RemoteWorkshopVersion> { WorkshopVersion(1, deleted: true), WorkshopVersion(2) },
            new List<RemotePackageAlias> { new("stable", 1) });

        var result = WorkshopVersionResolver.Resolve(details, "stable");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }

    [Test]
    public void Resolve_AliasToLiveWorkshopVersion_ResolvesToTarget()
    {
        var details = Details(
            new List<RemoteWorkshopVersion> { WorkshopVersion(1), WorkshopVersion(2) },
            new List<RemotePackageAlias> { new("stable", 2) });

        var result = WorkshopVersionResolver.Resolve(details, "stable");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
    }

    [Test]
    public void Resolve_AliasToNonexistentWorkshopVersion_Fails()
    {
        var details = Details(
            new List<RemoteWorkshopVersion> { WorkshopVersion(1) },
            new List<RemotePackageAlias> { new("stable", 9) });

        var result = WorkshopVersionResolver.Resolve(details, "stable");

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("does not exist");
    }

    [Test]
    public void Resolve_UnknownSelector_Fails()
    {
        var details = Details(new List<RemoteWorkshopVersion> { WorkshopVersion(1) });

        var result = WorkshopVersionResolver.Resolve(details, "nope");

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("not a workshop version number or a known alias");
    }
}
