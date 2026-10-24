using Celbridge.Resources;
using Celbridge.Utilities;

namespace Celbridge.Tests.Utilities;

[TestFixture]
public class ResourceNameHelperTests
{
    [Test]
    public void GetNameSelectionRange_SelectsTheBasenameWithoutTheExtension()
    {
        var selectionRange = ResourceNameHelper.GetNameSelectionRange("report.md");

        selectionRange.Should().Be(0..6);
    }

    [Test]
    public void GetNameSelectionRange_SelectsTheWholeNameWhenThereIsNoExtension()
    {
        var selectionRange = ResourceNameHelper.GetNameSelectionRange("Documents");

        selectionRange.Should().Be(..);
    }

    [Test]
    public void GetNameSelectionRange_SelectsTheWholeNameOfADotfile()
    {
        // A leading dot starts the basename rather than an extension, so there is nothing to preserve.
        var selectionRange = ResourceNameHelper.GetNameSelectionRange(".gitignore");

        selectionRange.Should().Be(..);
    }

    [Test]
    public void GetNameSelectionRange_SelectsUpToTheLastDot()
    {
        var selectionRange = ResourceNameHelper.GetNameSelectionRange("archive.tar.gz");

        selectionRange.Should().Be(0..11);
    }

    [Test]
    public async Task GenerateUniqueDuplicateKey_UsesTheFirstCopyNameWhenItIsFree()
    {
        var resourceFileSystem = UseListing("notes/report.md");

        var keyResult = await ResourceNameHelper.GenerateUniqueDuplicateKeyAsync(
            ResourceKey.Create("notes/report.md"), resourceFileSystem);

        keyResult.Value.Should().Be(ResourceKey.Create("notes/report - Copy.md"));
    }

    [Test]
    public async Task GenerateUniqueDuplicateKey_SkipsNamesInUseInAnyLetterCase()
    {
        var resourceFileSystem = UseListing("notes/report.md", "notes/report - copy.md", "notes/report - Copy (2).md");

        var keyResult = await ResourceNameHelper.GenerateUniqueDuplicateKeyAsync(
            ResourceKey.Create("notes/report.md"), resourceFileSystem);

        keyResult.Value.Should().Be(ResourceKey.Create("notes/report - Copy (3).md"));
    }

    [Test]
    public async Task GenerateUniqueDuplicateKey_FailsWhenTheFolderCannotBeListed()
    {
        var resourceFileSystem = Substitute.For<IResourceFileSystem>();
        resourceFileSystem.EnumerateFolderAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<IReadOnlyList<FolderChild>>.Fail("Folder not found")));

        var keyResult = await ResourceNameHelper.GenerateUniqueDuplicateKeyAsync(
            ResourceKey.Create("notes/report.md"), resourceFileSystem);

        keyResult.IsFailure.Should().BeTrue();
    }

    // Returns a file system that lists the given files in every folder.
    private static IResourceFileSystem UseListing(params string[] paths)
    {
        var listing = paths
            .Select(path => new FolderChild(ResourceKey.Create(path), false, 0, DateTime.UtcNow, FileSystemAttributes.None))
            .ToList();

        var resourceFileSystem = Substitute.For<IResourceFileSystem>();
        resourceFileSystem.EnumerateFolderAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<IReadOnlyList<FolderChild>>.Ok(listing)));

        return resourceFileSystem;
    }
}
