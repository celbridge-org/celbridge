using Celbridge.Resources;
using Celbridge.Resources.Helpers;

namespace Celbridge.Tests.Resources;

/// <summary>
/// A cached folder keeps the folder's listing under the folder's lock, and counts the changes recorded to its names
/// without the lock. These tests pin when a lookup lists the folder again, and that recording a change never waits
/// for a listing. The listings here come from the test, not the disk.
/// </summary>
[TestFixture]
public class CachedFolderTests
{
    private const ulong NameHash = 1;

    [Test]
    public void MarkingTheFolderStaleDoesNotWaitForAListingInProgress()
    {
        // The watcher's thread marks folders stale. Waiting for a listing of a large folder stalls the watcher, and
        // its buffer overflows.
        var folder = CreateFolder();

        using var isListing = new ManualResetEventSlim();
        using var finishListing = new ManualResetEventSlim();
        var lookup = Task.Run(() => folder.FindName(NameHash, acceptListingsAfter: null, (_, _) =>
        {
            isListing.Set();
            finishListing.Wait();
            return null;
        }));

        try
        {
            isListing.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

            var markStale = Task.Run(folder.MarkStale);

            markStale.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        }
        finally
        {
            finishListing.Set();
            lookup.Wait();
        }
    }

    [Test]
    public void AChangeRecordedDuringAListingLeavesTheNewListingStale()
    {
        var folder = CreateFolder();
        var listingCount = 0;

        FolderListing ListFolder(string folderPath, int changeCount)
        {
            listingCount++;
            if (listingCount == 1)
            {
                folder.MarkStale();
            }
            return CreateListing(listingCount, changeCount);
        }

        folder.FindName(NameHash, acceptListingsAfter: null, ListFolder).Should().Be(NameLookup.Found);
        folder.FindName(NameHash, acceptListingsAfter: null, ListFolder).Should().Be(NameLookup.Found);

        listingCount.Should().Be(2);
    }

    [Test]
    public void AStaleListingStartedAfterTheTimestampAnswersWithoutAListing()
    {
        var folder = CreateFolder();
        var listingCount = 0;

        FolderListing ListFolder(string folderPath, int changeCount)
        {
            listingCount++;
            return CreateListing(timestamp: 5, changeCount);
        }

        folder.FindName(NameHash, acceptListingsAfter: null, ListFolder).Should().Be(NameLookup.Found);
        folder.MarkStale();

        folder.FindName(NameHash, new ListingTimestamp(4), ListFolder).Should().Be(NameLookup.Found);
        folder.FindName(NameHash + 1, new ListingTimestamp(4), ListFolder).Should().Be(NameLookup.Missing);
        listingCount.Should().Be(1);

        // A timestamp from after the listing started does not accept it.
        folder.FindName(NameHash, new ListingTimestamp(5), ListFolder).Should().Be(NameLookup.Found);
        listingCount.Should().Be(2);
    }

    private static CachedFolder CreateFolder()
    {
        return new CachedFolder("folder", parent: null, DateTimeOffset.UnixEpoch, new ListedNameCounter());
    }

    private static FolderListing CreateListing(long timestamp, int changeCount)
    {
        return new FolderListing(new HashSet<ulong> { NameHash }, new ListingTimestamp(timestamp), changeCount);
    }
}
