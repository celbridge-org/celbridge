using Celbridge.Packages;
using Celbridge.Tools;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for PackageVersionResolver — the version-or-alias selection shared by
/// package_install (rejects deleted targets) and package_delete (allows them).
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
    public void ResolveForInstall_Latest_SelectsHighestNonDeletedVersion()
    {
        var details = Details(new List<RemotePackageVersion>
        {
            Version(1),
            Version(2),
            Version(3, deleted: true),
        });

        var result = PackageVersionResolver.ResolveForInstall(details, "latest");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
    }

    [Test]
    public void ResolveForInstall_ExplicitDeletedVersion_Fails()
    {
        var details = Details(new List<RemotePackageVersion>
        {
            Version(1, deleted: true),
            Version(2),
        });

        var result = PackageVersionResolver.ResolveForInstall(details, "1");

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("deleted");
    }

    [Test]
    public void ResolveForInstall_AliasToDeletedVersion_Fails()
    {
        var details = Details(
            new List<RemotePackageVersion> { Version(1, deleted: true), Version(2) },
            new List<RemotePackageAlias> { new("stable", 1) });

        var result = PackageVersionResolver.ResolveForInstall(details, "stable");

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("deleted");
    }

    [Test]
    public void ResolveForInstall_UnknownSelector_Fails()
    {
        var details = Details(new List<RemotePackageVersion> { Version(1) });

        var result = PackageVersionResolver.ResolveForInstall(details, "nope");

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("not a version number or a known alias");
    }

    [Test]
    public void ResolveForDelete_Alias_ResolvesToTargetVersion()
    {
        var details = Details(
            new List<RemotePackageVersion> { Version(1), Version(2) },
            new List<RemotePackageAlias> { new("stable", 2) });

        var result = PackageVersionResolver.ResolveForDelete(details, "stable");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
    }

    [Test]
    public void ResolveForDelete_ExplicitDeletedVersion_ResolvesCleanly()
    {
        // Delete does not pre-reject a deleted target; the client reports the
        // already-deleted state instead.
        var details = Details(new List<RemotePackageVersion>
        {
            Version(1, deleted: true),
            Version(2),
        });

        var result = PackageVersionResolver.ResolveForDelete(details, "1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }

    [Test]
    public void ResolveForDelete_Latest_SelectsHighestNonDeletedVersion()
    {
        var details = Details(new List<RemotePackageVersion>
        {
            Version(1),
            Version(2),
            Version(3, deleted: true),
        });

        var result = PackageVersionResolver.ResolveForDelete(details, "latest");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
    }
}
