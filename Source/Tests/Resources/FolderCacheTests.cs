using Celbridge.Resources;
using Celbridge.Resources.Helpers;
using Microsoft.Extensions.Time.Testing;

namespace Celbridge.Tests.Resources;

/// <summary>
/// The folder cache stores each checked folder with the hashes of its stored names. These tests pin how a folder is
/// found and listed, and how recorded changes update the listings. They also pin how a folder is dropped with every
/// folder below, and how the cache clears past a budget.
/// </summary>
[TestFixture]
public class FolderCacheTests
{
    private string _backingFolderPath = null!;

    [SetUp]
    public void Setup()
    {
        var folderPath = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(FolderCacheTests));
        if (Directory.Exists(folderPath))
        {
            Directory.Delete(folderPath, true);
        }
        Directory.CreateDirectory(folderPath);

        _backingFolderPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_backingFolderPath))
        {
            Directory.Delete(_backingFolderPath, true);
        }
    }

    [Test]
    public void TryGetFindsACachedFolderOnlyByItsExactPath()
    {
        var cache = CreateCache();
        var backing = cache.Acquire(_backingFolderPath, parent: null);
        cache.Acquire(PathFor("docs"), backing);

        cache.TryGet(PathFor("docs")).Should().NotBeNull();
        cache.TryGet(PathFor("DOCS")).Should().BeNull();
    }

    [Test]
    public void FindNameListsAFolderOnce()
    {
        WriteFile("a.txt");
        WriteFile("b.txt");
        var cache = CreateCache();
        var backing = cache.Acquire(_backingFolderPath, parent: null);

        cache.FindName(backing, "a.txt").Should().Be(NameLookup.Found);
        cache.FindName(backing, "b.txt").Should().Be(NameLookup.Found);

        cache.ListingCount.Should().Be(1);
    }

    [Test]
    public void FindNameListsAgainBeforeANameCountsAsMissing()
    {
        WriteFile("a.txt");
        var cache = CreateCache();
        var backing = cache.Acquire(_backingFolderPath, parent: null);
        cache.FindName(backing, "a.txt").Should().Be(NameLookup.Found);

        // Created without telling the cache, so the first listing lacks the name.
        WriteFile("created.txt");

        cache.FindName(backing, "created.txt").Should().Be(NameLookup.Found);
        cache.FindName(backing, "missing.txt").Should().Be(NameLookup.Missing);
        cache.ListingCount.Should().Be(3);
    }

    [Test]
    public void FindNameReportsAFolderThatCannotBeListed()
    {
        var cache = CreateCache();
        var missingFolder = cache.Acquire(PathFor("gone"), parent: null);

        cache.FindName(missingFolder, "a.txt").Should().Be(NameLookup.Unlisted);
    }

    [Test]
    public void RecordItemCreatedAddsTheOutermostNewFolderToTheFirstCachedListing()
    {
        WriteFile("a.txt");
        var cache = CreateCache();
        var backing = cache.Acquire(_backingFolderPath, parent: null);
        cache.FindName(backing, "a.txt").Should().Be(NameLookup.Found);

        WriteFile("docs/new/b.txt");
        cache.RecordItemCreated(PathFor("docs/new/b.txt"));

        cache.FindName(backing, "docs").Should().Be(NameLookup.Found);
        cache.ListingCount.Should().Be(1);
    }

    [Test]
    public void RecordItemRemovedTakesTheNameOutOfTheListing()
    {
        WriteFile("a.txt");
        var cache = CreateCache();
        var backing = cache.Acquire(_backingFolderPath, parent: null);
        cache.FindName(backing, "a.txt").Should().Be(NameLookup.Found);

        // The file stays on disk, so only a fresh listing finds the name again.
        cache.RecordItemRemoved(PathFor("a.txt"));

        cache.FindName(backing, "a.txt").Should().Be(NameLookup.Found);
        cache.ListingCount.Should().Be(2);
    }

    [Test]
    public void RecordItemRemovedDropsTheFolderAndEveryFolderBelowIt()
    {
        var cache = CreateCache();
        var backing = cache.Acquire(_backingFolderPath, parent: null);
        var docs = cache.Acquire(PathFor("docs"), backing);
        cache.Acquire(PathFor("docs/sub"), docs);
        cache.Acquire(PathFor("other"), backing);

        cache.RecordItemRemoved(PathFor("docs"));

        cache.Count.Should().Be(2);
        cache.TryGet(PathFor("docs/sub")).Should().BeNull();
        cache.TryGet(PathFor("other")).Should().NotBeNull();
    }

    [Test]
    public void RecordItemRemovedForTheBackingFolderClearsTheCache()
    {
        var cache = CreateCache();
        var backing = cache.Acquire(_backingFolderPath, parent: null);
        cache.Acquire(PathFor("docs"), backing);

        cache.RecordItemRemoved(_backingFolderPath);

        cache.Count.Should().Be(0);
    }

    [Test]
    public void AnExpiredFolderIsDroppedWithEveryFolderBelowIt()
    {
        var timeProvider = new FakeTimeProvider();
        var cache = CreateCache(ListingPolicy.Expiring, timeProvider);
        var backing = cache.Acquire(_backingFolderPath, parent: null);
        var docs = cache.Acquire(PathFor("docs"), backing);
        cache.Acquire(PathFor("docs/sub"), docs);

        timeProvider.Advance(TimeSpan.FromSeconds(30));
        cache.TryGet(PathFor("docs")).Should().NotBeNull();

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        cache.TryGet(PathFor("docs")).Should().BeNull();

        // No lookup has reached the backing folder since the folder expired, so the backing folder is still cached.
        cache.Count.Should().Be(1);
    }

    [Test]
    public void ReachingTheFolderBudgetClearsTheCache()
    {
        var cache = CreateCache(maxCachedFolders: 3);
        var backing = cache.Acquire(_backingFolderPath, parent: null);
        cache.Acquire(PathFor("a"), backing);
        cache.Count.Should().Be(2);

        cache.Acquire(PathFor("b"), backing);

        cache.Count.Should().Be(0);
    }

    [Test]
    public void PassingTheNameBudgetClearsTheCache()
    {
        WriteFile("a.txt");
        WriteFile("b.txt");
        WriteFile("c.txt");
        var cache = CreateCache(maxListedNames: 2);
        var backing = cache.Acquire(_backingFolderPath, parent: null);

        // The lookup in progress still uses the listing that passed the budget.
        cache.FindName(backing, "a.txt").Should().Be(NameLookup.Found);

        cache.Count.Should().Be(0);
    }

    [Test]
    public void AcquireReturnsTheFolderCachedUnderTheSamePath()
    {
        var cache = CreateCache();
        var backing = cache.Acquire(_backingFolderPath, parent: null);
        var docs = cache.Acquire(PathFor("docs"), backing);

        cache.Acquire(PathFor("docs"), backing).Should().BeSameAs(docs);
        cache.Count.Should().Be(2);
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = "Paths that differ only in letter case are different keys on Linux.")]
    public void SiblingsThatDifferOnlyInLetterCaseAreBothCached()
    {
        var cache = CreateCache();
        var backing = cache.Acquire(_backingFolderPath, parent: null);
        var lower = cache.Acquire(PathFor("docs"), backing);
        var upper = cache.Acquire(PathFor("DOCS"), backing);

        upper.Should().NotBeSameAs(lower);
        cache.TryGet(PathFor("docs")).Should().BeSameAs(lower);
        cache.TryGet(PathFor("DOCS")).Should().BeSameAs(upper);
        cache.Count.Should().Be(3);

        // A change reported in any letter case reaches both siblings.
        cache.RecordItemRemoved(PathFor("Docs"));

        cache.Count.Should().Be(1);
    }

    [Test]
    public void RecordNameChangedMakesTheListingStale()
    {
        WriteFile("a.txt");
        var cache = CreateCache();
        var backing = cache.Acquire(_backingFolderPath, parent: null);
        cache.FindName(backing, "a.txt").Should().Be(NameLookup.Found);

        cache.RecordNameChanged(PathFor("b.txt"));

        cache.FindName(backing, "a.txt").Should().Be(NameLookup.Found);
        cache.ListingCount.Should().Be(2);
    }

    [Test]
    public void FindNameAcceptsAStaleListingStartedAfterTheTimestamp()
    {
        WriteFile("a.txt");
        var cache = CreateCache();
        var backing = cache.Acquire(_backingFolderPath, parent: null);
        var timestamp = cache.GetListingTimestamp();
        cache.FindName(backing, "a.txt", timestamp).Should().Be(NameLookup.Found);

        cache.RecordNameChanged(PathFor("b.txt"));

        // The listing answers both ways for a lookup given the timestamp.
        cache.FindName(backing, "a.txt", timestamp).Should().Be(NameLookup.Found);
        cache.FindName(backing, "missing.txt", timestamp).Should().Be(NameLookup.Missing);
        cache.ListingCount.Should().Be(1);

        // A lookup without the timestamp lists the stale folder again.
        cache.FindName(backing, "a.txt").Should().Be(NameLookup.Found);
        cache.ListingCount.Should().Be(2);
    }

    [Test]
    public void FindNameListsAgainForATimestampReadAfterTheListing()
    {
        WriteFile("a.txt");
        var cache = CreateCache();
        var backing = cache.Acquire(_backingFolderPath, parent: null);
        cache.FindName(backing, "a.txt").Should().Be(NameLookup.Found);

        WriteFile("b.txt");
        cache.RecordNameChanged(PathFor("b.txt"));
        var timestamp = cache.GetListingTimestamp();

        cache.FindName(backing, "b.txt", timestamp).Should().Be(NameLookup.Found);
        cache.FindName(backing, "a.txt", timestamp).Should().Be(NameLookup.Found);
        cache.ListingCount.Should().Be(2);
    }

    [Test]
    public void TheNonePolicyKeepsNoListings()
    {
        var cache = CreateCache(ListingPolicy.None);
        var backing = cache.Acquire(_backingFolderPath, parent: null);

        cache.RecordItemCreated(PathFor("a.txt"));

        cache.HasListings.Should().BeFalse();
        cache.TryGet(_backingFolderPath).Should().BeSameAs(backing);
        cache.ListingCount.Should().Be(0);
    }

    private FolderCache CreateCache(
        ListingPolicy listingPolicy = ListingPolicy.UntilChanged,
        TimeProvider? timeProvider = null,
        int maxCachedFolders = FolderCache.DefaultMaxCachedFolders,
        int maxListedNames = FolderCache.DefaultMaxListedNames)
    {
        return new FolderCache(
            _backingFolderPath,
            listingPolicy,
            timeProvider ?? TimeProvider.System,
            maxCachedFolders,
            maxListedNames);
    }

    private string PathFor(string relativePath)
    {
        return Path.Combine(_backingFolderPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private void WriteFile(string relativePath)
    {
        var filePath = PathFor(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, "content");
    }
}
