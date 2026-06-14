using Celbridge.Packages;
using Celbridge.Tools;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for PackageHistoryFile — the HISTORY.md changelog rendered on install
/// and publish, and the installed-version read-back that package_status and the
/// replace confirmation rely on.
/// </summary>
[TestFixture]
public class PackageHistoryFileTests
{
    private static RemotePackageVersion MakeVersion(
        int version,
        string author = "Acme",
        string contentHash = "sha256:abc",
        string summary = "Change summary.",
        bool deleted = false)
    {
        var date = new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc);
        return new RemotePackageVersion(version, author, date, deleted, contentHash, summary);
    }

    [Test]
    public void Format_OrdersNewestFirst_SoInstalledVersionIsTheFirstHeading()
    {
        var versions = new List<RemotePackageVersion>
        {
            MakeVersion(1),
            MakeVersion(2),
            MakeVersion(3),
        };

        var markdown = PackageHistoryFile.Format(versions, installedVersion: 3);

        markdown.Should().StartWith("# 3");
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

        var markdown = PackageHistoryFile.Format(versions, installedVersion: 2);

        markdown.Should().Contain("# 2");
        markdown.Should().Contain("# 1");
        markdown.Should().NotContain("# 3");
        PackageHistoryFile.TryReadInstalledVersion(markdown).Should().Be(2);
    }

    [Test]
    public void Format_IncludesAuthorDateContentHashAndSummary()
    {
        var versions = new List<RemotePackageVersion>
        {
            MakeVersion(1, author: "Acme", contentHash: "sha256:abc123", summary: "Initial release."),
        };

        var markdown = PackageHistoryFile.Format(versions, installedVersion: 1);

        markdown.Should().Contain("Published by Acme on 2026-06-13.");
        markdown.Should().Contain("sha256:abc123");
        markdown.Should().Contain("Initial release.");
    }

    [Test]
    public void Format_OmitsContentHashLine_WhenHashIsBlank()
    {
        var versions = new List<RemotePackageVersion>
        {
            MakeVersion(1, contentHash: string.Empty),
        };

        var markdown = PackageHistoryFile.Format(versions, installedVersion: 1);

        markdown.Should().Contain("Published by Acme on 2026-06-13.");
        markdown.Should().NotContain("sha256");
    }

    [Test]
    public void Format_DeletedVersion_RendersSentinelInPlaceOfSummary_AndKeepsMetadata()
    {
        var versions = new List<RemotePackageVersion>
        {
            MakeVersion(1, contentHash: "sha256:keep", summary: "Original summary.", deleted: true),
            MakeVersion(2, summary: "Live summary."),
        };

        var markdown = PackageHistoryFile.Format(versions, installedVersion: 2);

        // The deleted version keeps its heading and content hash for provenance,
        // but its publisher summary is replaced by the sentinel.
        markdown.Should().Contain("# 1");
        markdown.Should().Contain("sha256:keep");
        markdown.Should().Contain("[package_deleted]");
        markdown.Should().NotContain("Original summary.");
        // The live version still renders its real summary.
        markdown.Should().Contain("Live summary.");
    }

    [Test]
    public void Format_DeletedVersion_HeadingHasNoSuffix()
    {
        var versions = new List<RemotePackageVersion>
        {
            MakeVersion(5, deleted: true),
        };

        var markdown = PackageHistoryFile.Format(versions, installedVersion: 5);

        markdown.Should().StartWith("# 5\r\n");
        markdown.Should().NotContain("tombstoned");
        PackageHistoryFile.TryReadInstalledVersion(markdown).Should().Be(5);
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
}
