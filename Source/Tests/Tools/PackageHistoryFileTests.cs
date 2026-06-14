using Celbridge.Packages;
using Celbridge.Tools;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for PackageHistoryFile — the HISTORY.md changelog rendered on install
/// and publish, the installed-reference read-back that package_status and the
/// replace confirmation rely on, and the stale-base publish check.
/// </summary>
[TestFixture]
public class PackageHistoryFileTests
{
    private const string PackageName = "sample-package";

    private static RemotePackageVersion MakeVersion(
        int version,
        string author = "Acme",
        string contentHash = "abc123abc123def",
        string summary = "Change summary.",
        bool deleted = false,
        DateTime? date = null)
    {
        var versionDate = date ?? new DateTime(2026, 6, 13, 15, 14, 50, DateTimeKind.Utc);
        return new RemotePackageVersion(version, author, versionDate, deleted, contentHash, summary);
    }

    [Test]
    public void Format_HeaderCarriesNameAtVersionToken_NewestFirst()
    {
        var versions = new List<RemotePackageVersion>
        {
            MakeVersion(1),
            MakeVersion(2),
            MakeVersion(3),
        };

        var markdown = PackageHistoryFile.Format(PackageName, versions, installedVersion: 3);

        markdown.Should().StartWith("# sample-package@3");
        PackageHistoryFile.TryReadInstalledVersion(markdown).Should().Be(3);
    }

    [Test]
    public void Format_ExcludesVersionsNewerThanTheInstalledOne()
    {
        var versions = new List<RemotePackageVersion>
        {
            MakeVersion(1),
            MakeVersion(2),
            MakeVersion(3),
        };

        var markdown = PackageHistoryFile.Format(PackageName, versions, installedVersion: 2);

        markdown.Should().Contain("# sample-package@2");
        markdown.Should().Contain("# sample-package@1");
        markdown.Should().NotContain("# sample-package@3");
    }

    [Test]
    public void Format_MetadataLine_CarriesFullUtcTimestampAuthorAndShortHash()
    {
        var versions = new List<RemotePackageVersion>
        {
            MakeVersion(1, author: "Celbridge", contentHash: "eb1ddd1ce6a9bbbb", summary: "Initial release."),
        };

        var markdown = PackageHistoryFile.Format(PackageName, versions, installedVersion: 1);

        // Full timestamp with a Z suffix, not date-only: versions published the
        // same day must stay distinguishable and ordered.
        markdown.Should().Contain("[time: 2026-06-13T15:14:50Z, author: Celbridge, hash: eb1ddd1ce6a9]");
        markdown.Should().Contain("Initial release.");
    }

    [Test]
    public void Format_ShortHash_StripsAlgorithmPrefixAndTruncatesTo12()
    {
        var versions = new List<RemotePackageVersion>
        {
            MakeVersion(1, contentHash: "sha256:0123456789abcdef0123"),
        };

        var markdown = PackageHistoryFile.Format(PackageName, versions, installedVersion: 1);

        markdown.Should().Contain("hash: 0123456789ab");
        markdown.Should().NotContain("sha256:");
    }

    [Test]
    public void Format_OmitsHashField_WhenHashIsBlank()
    {
        var versions = new List<RemotePackageVersion>
        {
            MakeVersion(1, contentHash: string.Empty),
        };

        var markdown = PackageHistoryFile.Format(PackageName, versions, installedVersion: 1);

        markdown.Should().Contain("time: 2026-06-13T15:14:50Z");
        markdown.Should().NotContain("hash:");
    }

    [Test]
    public void Format_DeletedVersion_RendersDeletedFlagAndSentinel()
    {
        var versions = new List<RemotePackageVersion>
        {
            MakeVersion(1, contentHash: "keepkeepkeep11", summary: "Original summary.", deleted: true),
            MakeVersion(2, summary: "Live summary."),
        };

        var markdown = PackageHistoryFile.Format(PackageName, versions, installedVersion: 2);

        // The deleted version keeps its heading, time, and hash for provenance,
        // gains a deleted flag, and renders the sentinel instead of its summary.
        markdown.Should().Contain("# sample-package@1");
        markdown.Should().Contain("hash: keepkeepkeep");
        markdown.Should().Contain("deleted: true");
        markdown.Should().Contain("[package_deleted]");
        markdown.Should().NotContain("Original summary.");
        markdown.Should().Contain("Live summary.");
    }

    [Test]
    public void TryReadInstalledReference_ParsesNameAndVersion()
    {
        var versions = new List<RemotePackageVersion> { MakeVersion(7) };
        var markdown = PackageHistoryFile.Format(PackageName, versions, installedVersion: 7);

        var reference = PackageHistoryFile.TryReadInstalledReference(markdown);

        reference.Should().NotBeNull();
        reference!.Name.Should().Be(PackageName);
        reference.Version.Should().Be(7);
    }

    [Test]
    public void TryReadInstalledReference_VersionOnlyHeading_ReturnsNull()
    {
        // A bare "# version" heading is not a valid entry; only "name@version" is.
        PackageHistoryFile.TryReadInstalledReference("# 5\r\n\r\nSome notes.\r\n").Should().BeNull();
    }

    [Test]
    public void TryReadInstalledVersion_ReturnsNull_WhenFirstLineIsNotAVersionHeading()
    {
        PackageHistoryFile.TryReadInstalledVersion("Some hand-authored notes.\r\n").Should().BeNull();
    }

    [Test]
    public void TryReadInstalledVersion_ReturnsNull_ForEmptyContent()
    {
        PackageHistoryFile.TryReadInstalledVersion(string.Empty).Should().BeNull();
    }

    [Test]
    public void IsStaleBase_SamePackageOlderThanLatest_IsStale()
    {
        var installed = new InstalledPackageReference(PackageName, 4);

        PackageHistoryFile.IsStaleBase(installed, PackageName, latestLiveVersion: 6).Should().BeTrue();
    }

    [Test]
    public void IsStaleBase_SamePackageAtLatest_IsNotStale()
    {
        var installed = new InstalledPackageReference(PackageName, 6);

        PackageHistoryFile.IsStaleBase(installed, PackageName, latestLiveVersion: 6).Should().BeFalse();
    }

    [Test]
    public void IsStaleBase_DifferentPackage_IsNotStale()
    {
        // A different recorded name is a rename or fork, not a lost-update race.
        var installed = new InstalledPackageReference("other-package", 1);

        PackageHistoryFile.IsStaleBase(installed, PackageName, latestLiveVersion: 6).Should().BeFalse();
    }

    [Test]
    public void IsStaleBase_NoInstallRecord_IsNotStale()
    {
        PackageHistoryFile.IsStaleBase(null, PackageName, latestLiveVersion: 6).Should().BeFalse();
    }
}
