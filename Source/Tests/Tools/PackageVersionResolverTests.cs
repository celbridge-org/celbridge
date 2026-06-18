using Celbridge.Packages;
using Celbridge.Tools;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for PackageVersionResolver — the version-or-alias selection shared by
/// package_install and package_delete. 'latest' selects the highest live
/// version; a number or alias dereferences to its target regardless of deletion,
/// leaving the download or delete to surface a deleted target.
/// </summary>
[TestFixture]
public class PackageVersionResolverTests
{
    private static RemotePackageVersion Version(int version, bool deleted = false)
    {
        var date = new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc);
        return new RemotePackageVersion(version, "Acme", date, deleted, "sha256:abc", "Summary.");
    }

    private static RemotePackageDetails Details(
        IReadOnlyList<RemotePackageVersion> versions,
        IReadOnlyList<RemotePackageAlias>? aliases = null)
    {
        var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return new RemotePackageDetails("my-widget", createdAt, versions, aliases ?? Array.Empty<RemotePackageAlias>());
    }

    [Test]
    public void Resolve_Latest_SelectsHighestNonDeletedVersion()
    {
        var details = Details(new List<RemotePackageVersion>
        {
            Version(1),
            Version(2),
            Version(3, deleted: true),
        });

        var result = PackageVersionResolver.Resolve(details, "latest");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
    }

    [Test]
    public void Resolve_LatestWithAllVersionsDeleted_Fails()
    {
        var details = Details(new List<RemotePackageVersion>
        {
            Version(1, deleted: true),
            Version(2, deleted: true),
        });

        var result = PackageVersionResolver.Resolve(details, "latest");

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("no live version");
    }

    [Test]
    public void Resolve_ExplicitDeletedVersion_ResolvesToTarget()
    {
        // Resolution does not consider liveness; the download surfaces the deletion.
        var details = Details(new List<RemotePackageVersion>
        {
            Version(1, deleted: true),
            Version(2),
        });

        var result = PackageVersionResolver.Resolve(details, "1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }

    [Test]
    public void Resolve_AliasToDeletedVersion_ResolvesToTarget()
    {
        var details = Details(
            new List<RemotePackageVersion> { Version(1, deleted: true), Version(2) },
            new List<RemotePackageAlias> { new("stable", 1) });

        var result = PackageVersionResolver.Resolve(details, "stable");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }

    [Test]
    public void Resolve_AliasToLiveVersion_ResolvesToTarget()
    {
        var details = Details(
            new List<RemotePackageVersion> { Version(1), Version(2) },
            new List<RemotePackageAlias> { new("stable", 2) });

        var result = PackageVersionResolver.Resolve(details, "stable");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
    }

    [Test]
    public void Resolve_AliasToNonexistentVersion_Fails()
    {
        var details = Details(
            new List<RemotePackageVersion> { Version(1) },
            new List<RemotePackageAlias> { new("stable", 9) });

        var result = PackageVersionResolver.Resolve(details, "stable");

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("does not exist");
    }

    [Test]
    public void Resolve_UnknownSelector_Fails()
    {
        var details = Details(new List<RemotePackageVersion> { Version(1) });

        var result = PackageVersionResolver.Resolve(details, "nope");

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("not a version number or a known alias");
    }
}
