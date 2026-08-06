using Celbridge.WebHost;

namespace Celbridge.Tests.WebHost;

[TestFixture]
public class FocusDerivationTests
{
    [Test]
    public void Derive_WebSurfaceHoldsFocus_FocusesSurfaceWithManagedFocusParked()
    {
        var desiredFocus = FocusDerivation.Derive(webSurfaceHoldsFocus: true);

        desiredFocus.FocusWebSurface.Should().BeTrue();
        desiredFocus.ParkManagedFocus.Should().BeTrue();
    }

    [Test]
    public void Derive_NoWebSurfaceHoldsFocus_LeavesManagedFocusAndReturnsNativeFocusToContent()
    {
        var desiredFocus = FocusDerivation.Derive(webSurfaceHoldsFocus: false);

        desiredFocus.FocusWebSurface.Should().BeFalse();
        desiredFocus.ParkManagedFocus.Should().BeFalse();
    }
}
