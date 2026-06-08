using Celbridge.DataTransfer;
using Celbridge.Resources;
using Celbridge.Resources.Helpers;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for the shared guard that refuses to move or copy a .cel sidecar on
/// its own. Sidecars follow their parent via the cascade, so a direct transfer
/// of a .cel key would orphan or duplicate it.
/// </summary>
[TestFixture]
public class SidecarTransferGuardTests
{
    [Test]
    public void DenySidecarSource_ReturnsFailureNamingTheMode_ForSidecarKey()
    {
        var sidecarService = Substitute.For<ISidecarService>();
        var source = new ResourceKey("Data/notes.md.cel");
        sidecarService.IsSidecarKey(source).Returns(true);

        var denial = SidecarTransferGuard.DenySidecarSource(sidecarService, source, DataTransferMode.Move);

        denial.Should().NotBeNull();
        denial!.IsFailure.Should().BeTrue();
        denial.FirstErrorMessage.Should().Contain("reserved");
        denial.FirstErrorMessage.Should().Contain("move");
    }

    [Test]
    public void DenySidecarSource_ReturnsNull_ForRegularKey()
    {
        var sidecarService = Substitute.For<ISidecarService>();
        var source = new ResourceKey("Data/notes.md");
        sidecarService.IsSidecarKey(source).Returns(false);

        var denial = SidecarTransferGuard.DenySidecarSource(sidecarService, source, DataTransferMode.Copy);

        denial.Should().BeNull();
    }
}
