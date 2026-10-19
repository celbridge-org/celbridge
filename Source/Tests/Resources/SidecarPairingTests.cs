using Celbridge.Resources;
using Celbridge.Resources.Helpers;
using Celbridge.Resources.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for SidecarPairing, which pairs each sidecar in a list of files with the file it describes.
/// </summary>
[TestFixture]
public class SidecarPairingTests
{
    [Test]
    public void Pair_PairsAFileWithTheSidecarNamedForIt()
    {
        var pairs = Pair("docs/photo.png", "docs/photo.png.cel", "readme.md");

        pairs.Files.Should().Equal(new ResourceKey("docs/photo.png"), new ResourceKey("readme.md"));
        pairs.FilesWithSidecars.Should().BeEquivalentTo(new[] { new ResourceKey("docs/photo.png") });
        pairs.Orphans.Should().BeEmpty();
    }

    [Test]
    public void Pair_ASidecarWithoutItsFile_IsAnOrphan()
    {
        var pairs = Pair("docs/photo.png.cel", "photo.png");

        pairs.FilesWithSidecars.Should().BeEmpty();
        pairs.Orphans.Should().Equal(new ResourceKey("docs/photo.png.cel"));
    }

    [Test]
    public void Pair_ASidecarInAnotherLetterCase_IsAnOrphan()
    {
        // The sidecar service finds a file's sidecar by the file's exact key, so the service never reads this sidecar.
        var pairs = Pair("docs/Photo.PNG", "docs/photo.png.cel");

        pairs.FilesWithSidecars.Should().BeEmpty();
        pairs.Orphans.Should().Equal(new ResourceKey("docs/photo.png.cel"));
    }

    [Test]
    public void Pair_ADoubleCelFile_IsAnInvalidSidecar()
    {
        var pairs = Pair("notes.cel", "notes.cel.cel");

        pairs.InvalidSidecars.Should().Equal(new ResourceKey("notes.cel.cel"));
        pairs.Orphans.Should().Equal(new ResourceKey("notes.cel"));
        pairs.Files.Should().BeEmpty();
    }

    private static SidecarPairs Pair(params string[] keys)
    {
        var sidecarService = new SidecarService(Substitute.For<IWorkspaceWrapper>());
        return SidecarPairing.Pair(keys.Select(key => new ResourceKey(key)), sidecarService);
    }
}
