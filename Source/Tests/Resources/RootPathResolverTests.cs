using Celbridge.Resources;
using Celbridge.Resources.Helpers;

namespace Celbridge.Tests.Resources;

[TestFixture]
public class RootPathResolverTests
{
    private const string CaseSensitiveVolumeReason = "On a case-sensitive volume, names that differ in case name different items.";

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan interval) => _now += interval;
    }

    private string? _projectFolder;
    private string? _outsideFolder;

    [SetUp]
    public void Setup()
    {
        _projectFolder = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(RootPathResolverTests)}");
        if (Directory.Exists(_projectFolder))
        {
            Directory.Delete(_projectFolder, true);
        }
        Directory.CreateDirectory(_projectFolder);

        _outsideFolder = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(RootPathResolverTests)}_links");
        if (Directory.Exists(_outsideFolder))
        {
            Directory.Delete(_outsideFolder, true);
        }
        Directory.CreateDirectory(_outsideFolder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolder))
        {
            Directory.Delete(_projectFolder!, true);
        }
        if (Directory.Exists(_outsideFolder))
        {
            Directory.Delete(_outsideFolder!, true);
        }
    }

    [Test]
    public void ValidateAndResolveSucceedsForValidKey()
    {
        Guard.IsNotNull(_projectFolder);

        var resolver = CreateResolver();
        var resourceKey = ResourceKey.Create("folder/file.txt");

        var resolveResult = resolver.ValidateAndResolve(resourceKey);

        var expectedPath = Path.GetFullPath(Path.Combine(_projectFolder, "folder", "file.txt"));
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().Be(expectedPath);
    }

    [Test]
    public void ValidateAndResolveSucceedsForEmptyKey()
    {
        Guard.IsNotNull(_projectFolder);

        var resolver = CreateResolver();

        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Empty);

        var expectedPath = Path.GetFullPath(_projectFolder);
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().Be(expectedPath);
    }

    [Test]
    public void ValidateAndResolveListsACachedFolderOnce()
    {
        WriteProjectFile("cached/a.txt");
        WriteProjectFile("cached/b.txt");
        var resolver = CreateResolver();

        // The first lookup lists the backing folder and the cached folder.
        resolver.ValidateAndResolve(ResourceKey.Create("cached/a.txt")).IsSuccess.Should().BeTrue();
        var listingCount = resolver.ListingCount;
        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("cached/b.txt"));

        resolveResult.IsSuccess.Should().BeTrue(resolveResult.FirstErrorMessage);
        listingCount.Should().Be(2);
        resolver.ListingCount.Should().Be(listingCount);
    }

    [Test]
    public void ValidateAndResolveFindsAFileCreatedOutsideAfterTheListing()
    {
        WriteProjectFile("docs/a.txt");
        var resolver = CreateResolver();
        resolver.ValidateAndResolve(ResourceKey.Create("docs/a.txt")).IsSuccess.Should().BeTrue();
        var listingCount = resolver.ListingCount;

        // Created without telling the resolver, so its name is missing from the listing.
        WriteProjectFile("docs/b.txt");
        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("docs/b.txt"));

        resolveResult.IsSuccess.Should().BeTrue(resolveResult.FirstErrorMessage);
        resolver.ListingCount.Should().Be(listingCount + 1);
    }

    [Test]
    public void ValidateAndResolveFindsARecordedFileWithoutListingAgain()
    {
        WriteProjectFile("docs/a.txt");
        var resolver = CreateResolver();
        resolver.ValidateAndResolve(ResourceKey.Create("docs/a.txt")).IsSuccess.Should().BeTrue();
        var listingCount = resolver.ListingCount;

        WriteProjectFile("docs/new/b.txt");
        resolver.RecordItemCreated(Path.Combine(_projectFolder!, "docs", "new", "b.txt"));
        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("docs/new"));

        resolveResult.IsSuccess.Should().BeTrue(resolveResult.FirstErrorMessage);
        resolver.ListingCount.Should().Be(listingCount);
    }

    [Test]
    public void InvalidateCacheClearsVerifiedFolders()
    {
        Guard.IsNotNull(_projectFolder);

        var subFolder = Path.Combine(_projectFolder, "ephemeral");
        Directory.CreateDirectory(subFolder);
        File.WriteAllText(Path.Combine(subFolder, "a.txt"), "test");

        var resolver = CreateResolver();

        // Cache the folder
        resolver.ValidateAndResolve(ResourceKey.Create("ephemeral/a.txt"));

        // Invalidate
        resolver.InvalidateCache();
        resolver.CachedFolderCount.Should().Be(0);

        // Next call should re-verify (still succeeds since folder is clean)
        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("ephemeral/a.txt"));
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().NotBeEmpty();
    }

    [Test]
    public void ValidateAndResolveRejectsReparsePoint()
    {
        Guard.IsNotNull(_projectFolder);

        var outsideFolder = Path.Combine(
            Path.GetTempPath(), $"Celbridge/{nameof(RootPathResolverTests)}_outside");
        Directory.CreateDirectory(outsideFolder);

        var symlinkPath = Path.Combine(_projectFolder, "link_folder");

        try
        {
            Directory.CreateSymbolicLink(symlinkPath, outsideFolder);
        }
        catch (Exception)
        {
            Assert.Ignore("Cannot create symbolic links on this system");
            return;
        }

        try
        {
            var resolver = CreateResolver();

            var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("link_folder/file.txt"));
            resolveResult.IsFailure.Should().BeTrue();
            resolveResult.FirstErrorMessage.Should().Contain("symbolic link or junction");
        }
        finally
        {
            if (Directory.Exists(symlinkPath))
            {
                Directory.Delete(symlinkPath);
            }
            if (Directory.Exists(outsideFolder))
            {
                Directory.Delete(outsideFolder, true);
            }
        }
    }

    [Test]
    public void ValidateAndResolveAcceptsNonExistentPath()
    {
        Guard.IsNotNull(_projectFolder);

        var resolver = CreateResolver();

        // Non-existent paths should be accepted (for create operations)
        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("new_folder/new_file.txt"));
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().NotBeEmpty();
    }

    [Test]
    public void GetResourceKeyReturnsRootOnlyKeyForBackingLocation()
    {
        Guard.IsNotNull(_projectFolder);

        var resolver = CreateResolver();

        var keyResult = resolver.GetResourceKey(_projectFolder);

        keyResult.IsSuccess.Should().BeTrue();
        keyResult.Value.Root.Should().Be(ResourceKey.DefaultRoot);
        keyResult.Value.Path.Should().BeEmpty();
    }

    [Test]
    public void GetResourceKeyComposesRelativeSegmentsUnderTheRoot()
    {
        Guard.IsNotNull(_projectFolder);

        var resolver = CreateResolver();
        var fullPath = Path.Combine(_projectFolder, "folder", "file.txt");

        var keyResult = resolver.GetResourceKey(fullPath);

        keyResult.IsSuccess.Should().BeTrue();
        keyResult.Value.Path.Should().Be("folder/file.txt");
    }

    [Test]
    public void GetResourceKeyFailsForPathOutsideBackingLocation()
    {
        Guard.IsNotNull(_projectFolder);

        var resolver = CreateResolver();
        var outsidePath = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(RootPathResolverTests)}_unrelated", "stray.txt");

        var keyResult = resolver.GetResourceKey(outsidePath);

        keyResult.IsFailure.Should().BeTrue();
        keyResult.FirstErrorMessage.Should().Contain("not under root");
    }

    [Test]
    public void GetResourceKeyCarriesThroughTheConfiguredRootName()
    {
        Guard.IsNotNull(_projectFolder);

        var resolver = CreateResolver("temp", ListingPolicy.None);
        var fullPath = Path.Combine(_projectFolder, "sub", "scratch.txt");

        var keyResult = resolver.GetResourceKey(fullPath);

        keyResult.IsSuccess.Should().BeTrue();
        keyResult.Value.Root.Should().Be("temp");
        keyResult.Value.Path.Should().Be("sub/scratch.txt");
    }

    [Test]
    public void GetResourceKeyMatchesCaseInsensitivelyWhenTheFileSystemIgnoresCase()
    {
        Guard.IsNotNull(_projectFolder);

        var resolver = CreateResolver();

        // A path whose backing-location portion differs only in case from how the
        // root was registered. On a case-insensitive volume (Windows, default APFS)
        // this is the same location, so it must still resolve under the root. The
        // relative segments keep their supplied casing. A case-sensitive volume
        // (Linux) correctly treats it as a different, out-of-root path.
        var differentlyCasedBacking = _projectFolder!.ToUpperInvariant();
        var fullPath = Path.Combine(differentlyCasedBacking, "folder", "file.txt");

        var keyResult = resolver.GetResourceKey(fullPath);

        if (OperatingSystem.IsLinux())
        {
            keyResult.IsFailure.Should().BeTrue();
            keyResult.FirstErrorMessage.Should().Contain("not under root");
        }
        else
        {
            keyResult.IsSuccess.Should().BeTrue();
            keyResult.Value.Path.Should().Be("folder/file.txt");
        }
    }

    [Test]
    public void ValidateAndResolveRejectsIntermediateReparsePoint()
    {
        Guard.IsNotNull(_projectFolder);

        var outsideFolder = Path.Combine(
            Path.GetTempPath(), $"Celbridge/{nameof(RootPathResolverTests)}_outside2");
        Directory.CreateDirectory(outsideFolder);

        // Create a structure: project/parent/link -> outside
        var parentFolder = Path.Combine(_projectFolder, "parent");
        Directory.CreateDirectory(parentFolder);

        var symlinkPath = Path.Combine(parentFolder, "link");

        try
        {
            Directory.CreateSymbolicLink(symlinkPath, outsideFolder);
        }
        catch (Exception)
        {
            Assert.Ignore("Cannot create symbolic links on this system");
            return;
        }

        try
        {
            var resolver = CreateResolver();

            var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("parent/link/file.txt"));
            resolveResult.IsFailure.Should().BeTrue();
            resolveResult.FirstErrorMessage.Should().Contain("symbolic link or junction");
        }
        finally
        {
            if (Directory.Exists(symlinkPath))
            {
                Directory.Delete(symlinkPath);
            }
            if (Directory.Exists(outsideFolder))
            {
                Directory.Delete(outsideFolder, true);
            }
        }
    }

    [Test]
    public void ValidateAndResolveRejectsALinkedFileOnAFirstLookup()
    {
        var linkKey = CreateLinkToOutsideFile("docs/link.txt");
        var resolver = CreateResolver();

        var resolveResult = resolver.ValidateAndResolve(linkKey);

        resolveResult.IsFailure.Should().BeTrue();
        resolveResult.FirstErrorMessage.Should().Contain("symbolic link or junction");
        resolveResult.FirstErrorMessage.Should().Contain("never follows links");
    }

    [Test]
    public void ValidateAndResolveRejectsALinkedFileAfterItsFolderHasBeenResolved()
    {
        var linkKey = CreateLinkToOutsideFile("docs/link.txt");
        var resolver = CreateResolver();
        resolver.ValidateAndResolve(ResourceKey.Create("docs")).IsSuccess.Should().BeTrue();

        var resolveResult = resolver.ValidateAndResolve(linkKey);

        resolveResult.IsFailure.Should().BeTrue();
        resolveResult.FirstErrorMessage.Should().Contain("symbolic link or junction");
    }

    [Test]
    public void ValidateAndResolveRejectsALinkedFileWhoseTargetIsInsideTheProject()
    {
        var docsFolder = Path.Combine(_projectFolder!, "docs");
        Directory.CreateDirectory(docsFolder);
        var targetPath = Path.Combine(docsFolder, "target.txt");
        File.WriteAllText(targetPath, "target");
        File.CreateSymbolicLink(Path.Combine(docsFolder, "link.txt"), targetPath);
        var resolver = CreateResolver();
        resolver.ValidateAndResolve(ResourceKey.Create("docs/target.txt")).IsSuccess.Should().BeTrue();

        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("docs/link.txt"));

        resolveResult.IsFailure.Should().BeTrue();
    }

    [Test]
    public void ValidateAndResolveRejectsALinkedFolder()
    {
        Directory.CreateSymbolicLink(Path.Combine(_projectFolder!, "linked"), _outsideFolder!);
        var resolver = CreateResolver();

        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("linked"));

        resolveResult.IsFailure.Should().BeTrue();
        resolveResult.FirstErrorMessage.Should().Contain("symbolic link or junction");
    }

    [Test]
    public void ValidateAndResolveRejectsABrokenLinkAfterItsFolderHasBeenResolved()
    {
        var docsFolder = Path.Combine(_projectFolder!, "docs");
        Directory.CreateDirectory(docsFolder);
        File.CreateSymbolicLink(Path.Combine(docsFolder, "broken.txt"), Path.Combine(_outsideFolder!, "gone.txt"));
        var resolver = CreateResolver();

        resolver.ValidateAndResolve(ResourceKey.Create("docs/broken.txt")).IsFailure.Should().BeTrue();
        resolver.ValidateAndResolve(ResourceKey.Create("docs")).IsSuccess.Should().BeTrue();
        resolver.ValidateAndResolve(ResourceKey.Create("docs/broken.txt")).IsFailure.Should().BeTrue();
    }

    [Test]
    public void ValidateAndResolveRejectsALinkCreatedWhereAMissingFolderWasResolved()
    {
        File.WriteAllText(Path.Combine(_outsideFolder!, "secret.txt"), "secret");
        var resolver = CreateResolver();
        resolver.ValidateAndResolve(ResourceKey.Create("missing/secret.txt")).IsSuccess.Should().BeTrue();

        Directory.CreateSymbolicLink(Path.Combine(_projectFolder!, "missing"), _outsideFolder!);
        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("missing/secret.txt"));

        resolveResult.IsFailure.Should().BeTrue();
        resolveResult.FirstErrorMessage.Should().Contain("symbolic link or junction");
    }

    [Test]
    public void ValidateAndResolveRejectsALinkThatReplacesACachedFolderOnceTheRemovalIsRecorded()
    {
        var subFolder = Path.Combine(_projectFolder!, "docs", "sub");
        Directory.CreateDirectory(subFolder);
        File.WriteAllText(Path.Combine(subFolder, "page.txt"), "inside");
        var outsideSubFolder = Path.Combine(_outsideFolder!, "sub");
        Directory.CreateDirectory(outsideSubFolder);
        File.WriteAllText(Path.Combine(outsideSubFolder, "page.txt"), "outside");
        var resolver = CreateResolver();
        resolver.ValidateAndResolve(ResourceKey.Create("docs/sub/page.txt")).IsSuccess.Should().BeTrue();

        // Replace docs, an ancestor of the cached folder, with a link to a folder of the same shape.
        var docsFolder = Path.Combine(_projectFolder!, "docs");
        Directory.Move(docsFolder, Path.Combine(_projectFolder!, "docs_moved"));
        Directory.CreateSymbolicLink(docsFolder, _outsideFolder!);
        resolver.RecordItemRemoved(docsFolder);
        resolver.RecordItemCreated(docsFolder);

        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("docs/sub/page.txt"));

        resolveResult.IsFailure.Should().BeTrue();
        resolveResult.FirstErrorMessage.Should().Contain("symbolic link or junction");
    }

    [Test]
    public void RecordItemRemovedForAFolderDropsEveryCachedFolderBelowIt()
    {
        WriteProjectFile("docs/sub/deeper/page.txt");
        WriteProjectFile("other/page.txt");
        var resolver = CreateResolver();
        resolver.ValidateAndResolve(ResourceKey.Create("docs/sub/deeper/page.txt")).IsSuccess.Should().BeTrue();
        resolver.ValidateAndResolve(ResourceKey.Create("other/page.txt")).IsSuccess.Should().BeTrue();

        // The cache has the backing folder, docs, sub, deeper and other.
        resolver.CachedFolderCount.Should().Be(5);

        resolver.RecordItemRemoved(Path.Combine(_projectFolder!, "docs"));

        resolver.CachedFolderCount.Should().Be(2);
    }

    [Test]
    public void RecordItemRemovedForAFileKeepsTheCachedFolders()
    {
        var docsFolder = Path.Combine(_projectFolder!, "docs");
        Directory.CreateDirectory(docsFolder);
        var plainPath = Path.Combine(docsFolder, "plain.txt");
        File.WriteAllText(plainPath, "plain");
        var resolver = CreateResolver();
        resolver.ValidateAndResolve(ResourceKey.Create("docs/plain.txt")).IsSuccess.Should().BeTrue();

        resolver.RecordItemRemoved(Path.Combine(docsFolder, "gone.txt"));

        resolver.CachedFolderCount.Should().Be(2);
    }

    [Test]
    public void ValidateAndResolveResolvesAPlainFileInAVerifiedFolder()
    {
        CreateLinkToOutsideFile("docs/link.txt");
        var plainPath = Path.Combine(_projectFolder!, "docs", "plain.txt");
        File.WriteAllText(plainPath, "plain");
        var resolver = CreateResolver();
        resolver.ValidateAndResolve(ResourceKey.Create("docs")).IsSuccess.Should().BeTrue();

        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("docs/plain.txt"));

        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().Be(Path.GetFullPath(plainPath));
    }

    [Test]
    public void IsPathSafeRejectsALinkedFileAfterItsFolderHasBeenResolved()
    {
        CreateLinkToOutsideFile("docs/link.txt");
        var resolver = CreateResolver();
        resolver.IsPathSafe(Path.Combine(_projectFolder!, "docs")).Should().BeTrue();

        resolver.IsPathSafe(Path.Combine(_projectFolder!, "docs", "link.txt")).Should().BeFalse();
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = CaseSensitiveVolumeReason)]
    public void ValidateAndResolveRefusesAKeyInTheWrongCaseAndNamesItsCanonicalForm()
    {
        WriteProjectFile("docs/notes.txt");
        var resolver = CreateResolver();

        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("DOCS/Notes.txt"));

        resolveResult.IsFailure.Should().BeTrue();
        resolveResult.HasException<StoredNameMismatchError>().Should().BeTrue();
        resolveResult.FirstErrorMessage.Should().Contain("does not match the name stored on disk");
        resolveResult.FirstErrorMessage.Should().Contain("Canonical form is 'project:docs/notes.txt'");
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = CaseSensitiveVolumeReason)]
    public void ValidateAndResolveRefusesANewKeyUnderAFolderInTheWrongCase()
    {
        WriteProjectFile("docs/notes.txt");
        var resolver = CreateResolver();

        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("Docs/new.txt"));

        resolveResult.IsFailure.Should().BeTrue();
        resolveResult.FirstErrorMessage.Should().Contain("Canonical form is 'project:docs/new.txt'");
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = CaseSensitiveVolumeReason)]
    public void ValidateAndResolveRefusesAFileInTheWrongCaseOnceItsFolderIsCached()
    {
        WriteProjectFile("docs/notes.txt");
        var resolver = CreateResolver();
        resolver.ValidateAndResolve(ResourceKey.Create("docs/notes.txt")).IsSuccess.Should().BeTrue();

        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("docs/NOTES.TXT"));

        resolveResult.IsFailure.Should().BeTrue();
        resolveResult.FirstErrorMessage.Should().Contain("Canonical form is 'project:docs/notes.txt'");
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = CaseSensitiveVolumeReason)]
    public void ValidateAndResolveRefusesAFolderInTheWrongCaseOnceTheRealFolderIsCached()
    {
        WriteProjectFile("docs/notes.txt");
        var resolver = CreateResolver();
        resolver.ValidateAndResolve(ResourceKey.Create("docs/notes.txt")).IsSuccess.Should().BeTrue();

        var folderResult = resolver.ValidateAndResolve(ResourceKey.Create("Docs"));
        var fileResult = resolver.ValidateAndResolve(ResourceKey.Create("Docs/notes.txt"));

        folderResult.IsFailure.Should().BeTrue();
        folderResult.FirstErrorMessage.Should().Contain("Canonical form is 'project:docs'");
        fileResult.IsFailure.Should().BeTrue();
        fileResult.FirstErrorMessage.Should().Contain("Canonical form is 'project:docs/notes.txt'");
    }

    [Test]
    public void ValidateAndResolveAcceptsANewKeyInAVerifiedFolder()
    {
        WriteProjectFile("docs/notes.txt");
        var resolver = CreateResolver();
        resolver.ValidateAndResolve(ResourceKey.Create("docs/notes.txt")).IsSuccess.Should().BeTrue();

        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("docs/New Notes.txt"));

        resolveResult.IsSuccess.Should().BeTrue(resolveResult.FirstErrorMessage);
        resolveResult.Value.Should().Be(Path.GetFullPath(Path.Combine(_projectFolder!, "docs", "New Notes.txt")));
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = CaseSensitiveVolumeReason)]
    public void ValidateAndResolveTrustsACachedFolderUntilTheRenameIsRecorded()
    {
        WriteProjectFile("docs/notes.txt");
        var resolver = CreateResolver();
        resolver.ValidateAndResolve(ResourceKey.Create("docs/notes.txt")).IsSuccess.Should().BeTrue();

        // Rename the folder without telling the resolver. The cached folder is not checked again.
        var oldFolderPath = Path.Combine(_projectFolder!, "docs");
        var newFolderPath = Path.Combine(_projectFolder!, "Docs");
        Directory.Move(oldFolderPath, newFolderPath);
        var cachedResult = resolver.ValidateAndResolve(ResourceKey.Create("docs/notes.txt"));

        resolver.RecordItemRemoved(oldFolderPath);
        resolver.RecordItemCreated(newFolderPath);
        var checkedResult = resolver.ValidateAndResolve(ResourceKey.Create("docs/notes.txt"));
        var renamedResult = resolver.ValidateAndResolve(ResourceKey.Create("Docs/notes.txt"));

        cachedResult.IsSuccess.Should().BeTrue();
        checkedResult.IsFailure.Should().BeTrue();
        checkedResult.FirstErrorMessage.Should().Contain("Canonical form is 'project:Docs/notes.txt'");
        renamedResult.IsSuccess.Should().BeTrue(renamedResult.FirstErrorMessage);
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = CaseSensitiveVolumeReason)]
    public void ValidateAndResolveAcceptsAStaleNameUntilTheRemovalIsRecorded()
    {
        WriteProjectFile("docs/notes.txt");
        var resolver = CreateResolver();
        resolver.ValidateAndResolve(ResourceKey.Create("docs/notes.txt")).IsSuccess.Should().BeTrue();

        // Rename the file by case alone without telling the resolver. The listing still has the old name.
        var oldFilePath = Path.Combine(_projectFolder!, "docs", "notes.txt");
        var newFilePath = Path.Combine(_projectFolder!, "docs", "Notes.txt");
        File.Move(oldFilePath, newFilePath);
        var staleResult = resolver.ValidateAndResolve(ResourceKey.Create("docs/notes.txt"));

        resolver.RecordItemRemoved(oldFilePath);
        resolver.RecordItemCreated(newFilePath);
        var checkedResult = resolver.ValidateAndResolve(ResourceKey.Create("docs/notes.txt"));

        staleResult.IsSuccess.Should().BeTrue();
        checkedResult.IsFailure.Should().BeTrue();
        checkedResult.FirstErrorMessage.Should().Contain("Canonical form is 'project:docs/Notes.txt'");
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = CaseSensitiveVolumeReason)]
    public void RecordNameChangedForBothNamesOfACaseOnlyRenameRefusesTheOldName()
    {
        WriteProjectFile("docs/notes.txt");
        var resolver = CreateResolver();
        resolver.ValidateAndResolve(ResourceKey.Create("docs/notes.txt")).IsSuccess.Should().BeTrue();

        // On macOS the watcher reports a create for both names of a rename that changes only letter case.
        var oldFilePath = Path.Combine(_projectFolder!, "docs", "notes.txt");
        var newFilePath = Path.Combine(_projectFolder!, "docs", "Notes.txt");
        File.Move(oldFilePath, newFilePath);
        resolver.RecordNameChanged(oldFilePath);
        resolver.RecordNameChanged(newFilePath);

        var oldResult = resolver.ValidateAndResolve(ResourceKey.Create("docs/notes.txt"));
        var newResult = resolver.ValidateAndResolve(ResourceKey.Create("docs/Notes.txt"));

        oldResult.IsFailure.Should().BeTrue();
        oldResult.FirstErrorMessage.Should().Contain("Canonical form is 'project:docs/Notes.txt'");
        newResult.IsSuccess.Should().BeTrue(newResult.FirstErrorMessage);
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = CaseSensitiveVolumeReason)]
    public void RecordNameChangedDropsTheListingOfAFolderNamedInAnotherCase()
    {
        WriteProjectFile("docs/notes.txt");
        WriteProjectFile("docs/other.txt");
        var resolver = CreateResolver();
        resolver.ValidateAndResolve(ResourceKey.Create("docs/notes.txt")).IsSuccess.Should().BeTrue();
        var listingCount = resolver.ListingCount;

        // On macOS the watcher reports a removed item in the letter case of the call that removed it.
        File.Delete(Path.Combine(_projectFolder!, "docs", "notes.txt"));
        resolver.RecordNameChanged(Path.Combine(_projectFolder!, "DOCS", "NOTES.TXT"));
        resolver.ValidateAndResolve(ResourceKey.Create("docs/other.txt")).IsSuccess.Should().BeTrue();

        resolver.ListingCount.Should().Be(listingCount + 1);
    }

    [Test]
    public void RecordNameChangedLeavesTheListingsOfOtherFolders()
    {
        WriteProjectFile("docs/notes.txt");
        WriteProjectFile("other/page.txt");
        var resolver = CreateResolver();
        resolver.ValidateAndResolve(ResourceKey.Create("docs/notes.txt")).IsSuccess.Should().BeTrue();
        resolver.ValidateAndResolve(ResourceKey.Create("other/page.txt")).IsSuccess.Should().BeTrue();
        var listingCount = resolver.ListingCount;

        resolver.RecordNameChanged(Path.Combine(_projectFolder!, "docs", "notes.txt"));
        resolver.ValidateAndResolve(ResourceKey.Create("other/page.txt")).IsSuccess.Should().BeTrue();

        resolver.ListingCount.Should().Be(listingCount);
    }

    [Test]
    public void RecordNameChangedRejectsALinkThatReplacesACachedFolder()
    {
        WriteProjectFile("docs/sub/page.txt");
        var outsideSubFolder = Path.Combine(_outsideFolder!, "sub");
        Directory.CreateDirectory(outsideSubFolder);
        File.WriteAllText(Path.Combine(outsideSubFolder, "page.txt"), "outside");
        var resolver = CreateResolver();
        resolver.ValidateAndResolve(ResourceKey.Create("docs/sub/page.txt")).IsSuccess.Should().BeTrue();

        // Replace docs, an ancestor of the cached folder, with a link to a folder of the same shape.
        var docsFolder = Path.Combine(_projectFolder!, "docs");
        Directory.Move(docsFolder, Path.Combine(_projectFolder!, "docs_moved"));
        Directory.CreateSymbolicLink(docsFolder, _outsideFolder!);
        resolver.RecordNameChanged(docsFolder);

        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("docs/sub/page.txt"));

        resolveResult.IsFailure.Should().BeTrue();
        resolveResult.FirstErrorMessage.Should().Contain("symbolic link or junction");
    }

    [Test]
    [Platform("MacOsX", Reason = "Only APFS finds a name stored in another Unicode normalization.")]
    public void ValidateAndResolveRefusesAKeyInAnotherUnicodeNormalization()
    {
        // The file's name is stored decomposed, and the key composes it.
        WriteProjectFile("Cafe\u0301.txt");
        var resolver = CreateResolver();

        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("Caf\u00e9.txt"));

        resolveResult.IsFailure.Should().BeTrue();
        resolveResult.FirstErrorMessage.Should().Contain("Canonical form is 'project:Cafe\u0301.txt'");
    }

    [Test]
    public void ValidateAndResolveAcceptsAKeyInTheStoredUnicodeNormalization()
    {
        WriteProjectFile("Cafe\u0301.txt");
        var resolver = CreateResolver();

        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("Cafe\u0301.txt"));

        resolveResult.IsSuccess.Should().BeTrue(resolveResult.FirstErrorMessage);
    }

    [Test]
    [Platform("MacOsX", Reason = "Only APFS finds a name stored in another Unicode normalization.")]
    public void ValidateAndResolveNamesTheStoredNormalizationWhenTheCaseIsWrong()
    {
        WriteProjectFile("Cafe\u0301.txt");
        var resolver = CreateResolver();

        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("CAF\u00c9.txt"));

        resolveResult.IsFailure.Should().BeTrue();
        resolveResult.FirstErrorMessage.Should().Contain("Canonical form is 'project:Cafe\u0301.txt'");
    }

    [Test]
    public void ValidateAndResolveAcceptsAnyFormOfANameAndListsNothingUnderTheNonePolicy()
    {
        WriteProjectFile("docs/notes.txt");
        var resolver = CreateResolver("temp", ListingPolicy.None);

        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("temp:DOCS/Notes.txt"));

        resolveResult.IsSuccess.Should().BeTrue(resolveResult.FirstErrorMessage);
        resolver.ListingCount.Should().Be(0);
    }

    [Test]
    public void ValidateAndResolveListsAgainOnceAnExpiringListingHasExpired()
    {
        WriteProjectFile("docs/notes.txt");
        var timeProvider = new FakeTimeProvider();
        var resolver = CreateResolver("logs", ListingPolicy.Expiring, timeProvider);
        resolver.ValidateAndResolve(ResourceKey.Create("logs:docs/notes.txt")).IsSuccess.Should().BeTrue();
        var listingCount = resolver.ListingCount;

        timeProvider.Advance(TimeSpan.FromSeconds(30));
        resolver.ValidateAndResolve(ResourceKey.Create("logs:docs/notes.txt")).IsSuccess.Should().BeTrue();
        var listingCountWithinLifetime = resolver.ListingCount;

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        resolver.ValidateAndResolve(ResourceKey.Create("logs:docs/notes.txt")).IsSuccess.Should().BeTrue();

        listingCount.Should().Be(2);
        listingCountWithinLifetime.Should().Be(listingCount);
        resolver.ListingCount.Should().Be(listingCount * 2);
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = CaseSensitiveVolumeReason)]
    public void ValidateAndResolveRejectsALinkThatReplacesAFolderResolvedInTheWrongCase()
    {
        WriteProjectFile("docs/sub/page.txt");
        var outsideSubFolder = Path.Combine(_outsideFolder!, "sub");
        Directory.CreateDirectory(outsideSubFolder);
        File.WriteAllText(Path.Combine(outsideSubFolder, "page.txt"), "outside");
        var resolver = CreateResolver("temp", ListingPolicy.None);
        resolver.ValidateAndResolve(ResourceKey.Create("temp:DOCS/sub/page.txt")).IsSuccess.Should().BeTrue();

        // The watcher reports the folder under its stored name.
        var docsFolder = Path.Combine(_projectFolder!, "docs");
        Directory.Move(docsFolder, Path.Combine(_projectFolder!, "docs_moved"));
        Directory.CreateSymbolicLink(docsFolder, _outsideFolder!);
        resolver.RecordItemRemoved(docsFolder);

        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("temp:DOCS/sub/page.txt"));

        resolveResult.IsFailure.Should().BeTrue();
        resolveResult.FirstErrorMessage.Should().Contain("symbolic link or junction");
    }

    [Test]
    public void ValidateAndResolveRefusesAKeyThatEndsInADotSegment()
    {
        WriteProjectFile("docs/notes.txt");
        var resolver = CreateResolver();

        // The file system reads docs/. as docs, so the key would name the folder by a second form of its path.
        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("docs/."));

        resolveResult.IsFailure.Should().BeTrue();
    }

    [Test]
    [Platform(Include = "Win", Reason = "Windows drops a trailing dot or space from a name.")]
    public void ValidateAndResolveRefusesANameThatWindowsTrims([Values("notes.txt.", "notes.txt ")] string keyPath)
    {
        WriteProjectFile("notes.txt");
        var resolver = CreateResolver();

        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create(keyPath));

        resolveResult.IsFailure.Should().BeTrue();
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = "Linux roots keep no listings.")]
    public void ValidateAndResolveListsTheRootOfAVolumeAgainOnceANameIsRemoved()
    {
        // The user profile is in a folder at the root of its volume: /Users on macOS, C:\Users on Windows.
        var profilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var volumeRoot = Path.GetPathRoot(profilePath)!;
        var topFolderName = Path.GetRelativePath(volumeRoot, profilePath).Split(Path.DirectorySeparatorChar)[0];
        var resolver = new RootPathResolver(ResourceKey.DefaultRoot, volumeRoot, ListingPolicy.UntilChanged, TimeProvider.System);
        var resourceKey = ResourceKey.Create(topFolderName);

        resolver.ValidateAndResolve(resourceKey).IsSuccess.Should().BeTrue();
        resolver.RecordItemRemoved(Path.Combine(volumeRoot, topFolderName));
        resolver.ValidateAndResolve(resourceKey).IsSuccess.Should().BeTrue();

        // The recorded removal took the name out of the root's listing, so the root was listed again.
        resolver.ListingCount.Should().Be(2);
    }

    private RootPathResolver CreateResolver(
        string rootName = ResourceKey.DefaultRoot,
        ListingPolicy listingPolicy = ListingPolicy.UntilChanged,
        TimeProvider? timeProvider = null)
    {
        return new RootPathResolver(rootName, _projectFolder!, listingPolicy, timeProvider ?? TimeProvider.System);
    }

    // Creates a link at the key's path in the project, pointing at a file outside the project.
    private ResourceKey CreateLinkToOutsideFile(string keyPath)
    {
        var targetPath = Path.Combine(_outsideFolder!, "secret.txt");
        File.WriteAllText(targetPath, "secret");

        var linkPath = Path.Combine(_projectFolder!, keyPath);
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        File.CreateSymbolicLink(linkPath, targetPath);

        return ResourceKey.Create(keyPath);
    }

    // Writes a file at the key's path in the project, creating its folders.
    private void WriteProjectFile(string keyPath)
    {
        var filePath = Path.Combine(_projectFolder!, keyPath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, "content");
    }
}
