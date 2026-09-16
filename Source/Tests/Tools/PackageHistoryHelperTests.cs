using Celbridge.Tools;
using Celbridge.Workshop;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for PackageHistoryHelper — the HISTORY.md changelog rendered on install
/// and publish, the installed-reference read-back that the replace confirmation
/// relies on, and the stale-base publish check.
/// </summary>
[TestFixture]
public class PackageHistoryHelperTests
{
    private const string PackageName = "sample-package";

    private static RemoteWorkshopVersion MakeWorkshopVersion(
        int workshopVersion,
        string author = "Acme",
        string contentHash = "abc123abc123def",
        string summary = "Change summary.",
        bool deleted = false,
        DateTime? date = null)
    {
        var versionDate = date ?? new DateTime(2026, 6, 13, 15, 14, 50, DateTimeKind.Utc);
        return new RemoteWorkshopVersion(workshopVersion, author, versionDate, deleted, contentHash, summary);
    }

    [Test]
    public void Format_HeaderCarriesNameAtVersionToken_NewestFirst()
    {
        var workshopVersions = new List<RemoteWorkshopVersion>
        {
            MakeWorkshopVersion(1),
            MakeWorkshopVersion(2),
            MakeWorkshopVersion(3),
        };

        var markdown = PackageHistoryHelper.Format(PackageName, workshopVersions, installedWorkshopVersion: 3).Value;

        markdown.Should().StartWith("# sample-package@3");
        PackageHistoryHelper.TryReadInstalledReference(markdown)!.WorkshopVersion.Should().Be(3);
    }

    [Test]
    public void Format_ExcludesWorkshopVersionsNewerThanTheInstalledOne()
    {
        var workshopVersions = new List<RemoteWorkshopVersion>
        {
            MakeWorkshopVersion(1),
            MakeWorkshopVersion(2),
            MakeWorkshopVersion(3),
        };

        var markdown = PackageHistoryHelper.Format(PackageName, workshopVersions, installedWorkshopVersion: 2).Value;

        markdown.Should().Contain("# sample-package@2");
        markdown.Should().Contain("# sample-package@1");
        markdown.Should().NotContain("# sample-package@3");
    }

    [Test]
    public void Format_MetadataLine_CarriesFullUtcTimestampAuthorAndShortHash()
    {
        var workshopVersions = new List<RemoteWorkshopVersion>
        {
            MakeWorkshopVersion(1, author: "Celbridge", contentHash: "eb1ddd1ce6a9bbbb", summary: "Initial release."),
        };

        var markdown = PackageHistoryHelper.Format(PackageName, workshopVersions, installedWorkshopVersion: 1).Value;

        // Full timestamp with a Z suffix, not date-only: versions published the
        // same day must stay distinguishable and ordered.
        markdown.Should().Contain("[time: 2026-06-13T15:14:50Z, author: Celbridge, hash: eb1ddd1ce6a9]");
        markdown.Should().Contain("Initial release.");
    }

    [Test]
    public void Format_ShortHash_StripsAlgorithmPrefixAndTruncatesTo12()
    {
        var workshopVersions = new List<RemoteWorkshopVersion>
        {
            MakeWorkshopVersion(1, contentHash: "sha256:0123456789abcdef0123"),
        };

        var markdown = PackageHistoryHelper.Format(PackageName, workshopVersions, installedWorkshopVersion: 1).Value;

        markdown.Should().Contain("hash: 0123456789ab");
        markdown.Should().NotContain("sha256:");
    }

    [Test]
    public void Format_OmitsHashField_WhenHashIsBlank()
    {
        var workshopVersions = new List<RemoteWorkshopVersion>
        {
            MakeWorkshopVersion(1, contentHash: string.Empty),
        };

        var markdown = PackageHistoryHelper.Format(PackageName, workshopVersions, installedWorkshopVersion: 1).Value;

        markdown.Should().Contain("time: 2026-06-13T15:14:50Z");
        markdown.Should().NotContain("hash:");
    }

    [Test]
    public void Format_DeletedWorkshopVersion_RendersDeletedFlagAndSentinel()
    {
        var workshopVersions = new List<RemoteWorkshopVersion>
        {
            MakeWorkshopVersion(1, contentHash: "keepkeepkeep11", summary: "Original summary.", deleted: true),
            MakeWorkshopVersion(2, summary: "Live summary."),
        };

        var markdown = PackageHistoryHelper.Format(PackageName, workshopVersions, installedWorkshopVersion: 2).Value;

        // The deleted workshop version keeps its heading, time, and hash for provenance,
        // gains a deleted flag, and renders the sentinel instead of its summary.
        markdown.Should().Contain("# sample-package@1");
        markdown.Should().Contain("hash: keepkeepkeep");
        markdown.Should().Contain("deleted: true");
        markdown.Should().Contain("[package_deleted]");
        markdown.Should().NotContain("Original summary.");
        markdown.Should().Contain("Live summary.");
    }

    [Test]
    public void Format_NoWorkshopVersionAtOrBelowInstalled_Fails()
    {
        // Only workshop version 5 exists, but the install record names workshop version 4,
        // so the filtered list is empty and the changelog would carry no entries.
        var workshopVersions = new List<RemoteWorkshopVersion> { MakeWorkshopVersion(5) };

        var result = PackageHistoryHelper.Format(PackageName, workshopVersions, installedWorkshopVersion: 4);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void TryReadInstalledReference_ParsesNameAndWorkshopVersion()
    {
        var workshopVersions = new List<RemoteWorkshopVersion> { MakeWorkshopVersion(7) };
        var markdown = PackageHistoryHelper.Format(PackageName, workshopVersions, installedWorkshopVersion: 7).Value;

        var reference = PackageHistoryHelper.TryReadInstalledReference(markdown);

        reference.Should().NotBeNull();
        reference!.Name.Should().Be(PackageName);
        reference.WorkshopVersion.Should().Be(7);
    }

    [Test]
    public void TryReadInstalledReference_VersionOnlyHeading_ReturnsNull()
    {
        // A bare "# version" heading is not a valid entry. Only "name@version" is.
        PackageHistoryHelper.TryReadInstalledReference("# 5\r\n\r\nSome notes.\r\n").Should().BeNull();
    }

    [Test]
    public void TryReadInstalledReference_ReturnsNull_WhenFirstLineIsNotAVersionHeading()
    {
        PackageHistoryHelper.TryReadInstalledReference("Some hand-authored notes.\r\n").Should().BeNull();
    }

    [Test]
    public void TryReadInstalledReference_ReturnsNull_ForEmptyContent()
    {
        PackageHistoryHelper.TryReadInstalledReference(string.Empty).Should().BeNull();
    }

    [Test]
    public void IsStaleBase_SamePackageOlderThanLatest_IsStale()
    {
        var installed = new InstalledPackageReference(PackageName, 4);

        PackageHistoryHelper.IsStaleBase(installed, PackageName, latestLiveWorkshopVersion: 6).Should().BeTrue();
    }

    [Test]
    public void IsStaleBase_SamePackageAtLatest_IsNotStale()
    {
        var installed = new InstalledPackageReference(PackageName, 6);

        PackageHistoryHelper.IsStaleBase(installed, PackageName, latestLiveWorkshopVersion: 6).Should().BeFalse();
    }

    [Test]
    public void IsStaleBase_DifferentPackage_IsNotStale()
    {
        // A different recorded name is a rename or fork, not a lost-update race.
        var installed = new InstalledPackageReference("other-package", 1);

        PackageHistoryHelper.IsStaleBase(installed, PackageName, latestLiveWorkshopVersion: 6).Should().BeFalse();
    }

    [Test]
    public void IsStaleBase_NoInstallRecord_IsNotStale()
    {
        PackageHistoryHelper.IsStaleBase(null, PackageName, latestLiveWorkshopVersion: 6).Should().BeFalse();
    }
}
