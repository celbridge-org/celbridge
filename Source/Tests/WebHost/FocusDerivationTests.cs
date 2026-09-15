using Celbridge.UserInterface;
using Celbridge.WebHost;

namespace Celbridge.Tests.WebHost;

[TestFixture]
public class FocusDerivationTests
{
    [Test]
    public void Derive_WebSurfaceHoldsFocus_FocusesSurfaceAndYieldsManagedFocus()
    {
        var desiredFocus = FocusDerivation.Derive(webSurfaceHoldsFocus: true, FocusLocation.MainContent);

        desiredFocus.FocusWebSurface.Should().BeTrue();
        desiredFocus.YieldManagedFocus.Should().BeTrue();
    }

    [Test]
    public void Derive_PopupHoldsFocus_LeavesFocusWithThePopup()
    {
        // A popup reports no panel, so the model still names the surface underneath it. Yielding managed
        // focus would pull it out of the popup, which then stops receiving input while still on screen.
        var desiredFocus = FocusDerivation.Derive(webSurfaceHoldsFocus: true, FocusLocation.Popup);

        desiredFocus.FocusWebSurface.Should().BeFalse();
        desiredFocus.YieldManagedFocus.Should().BeFalse();
    }

    [Test]
    public void Derive_NoWebSurfaceHoldsFocus_LeavesManagedFocusAndReturnsNativeFocusToContent()
    {
        var desiredFocus = FocusDerivation.Derive(webSurfaceHoldsFocus: false, FocusLocation.MainContent);

        desiredFocus.FocusWebSurface.Should().BeFalse();
        desiredFocus.YieldManagedFocus.Should().BeFalse();
    }

    [Test]
    public void Derive_ManagedFocusIsDetached_YieldsManagedFocus()
    {
        // A dismissed popup leaves managed focus on the item that was focused inside it, so the keys the
        // user types next reach a menu that is no longer on screen.
        var desiredFocus = FocusDerivation.Derive(webSurfaceHoldsFocus: false, FocusLocation.Detached);

        desiredFocus.FocusWebSurface.Should().BeFalse();
        desiredFocus.YieldManagedFocus.Should().BeTrue();
    }
}
