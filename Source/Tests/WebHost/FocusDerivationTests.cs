using Celbridge.WebHost;

namespace Celbridge.Tests.WebHost;

[TestFixture]
public class FocusDerivationTests
{
    [Test]
    public void Derive_WebSurfaceHoldsFocus_FocusesSurfaceAndYieldsManagedFocus()
    {
        var desiredFocus = FocusDerivation.Derive(webSurfaceHoldsFocus: true, popupHoldsFocus: false);

        desiredFocus.FocusWebSurface.Should().BeTrue();
        desiredFocus.YieldManagedFocus.Should().BeTrue();
    }

    [Test]
    public void Derive_PopupHoldsFocus_LeavesFocusWithThePopup()
    {
        // A popup reports no panel, so the model still names the surface underneath it. Yielding managed
        // focus would pull it out of the popup, which then stops receiving input while still on screen.
        var desiredFocus = FocusDerivation.Derive(webSurfaceHoldsFocus: true, popupHoldsFocus: true);

        desiredFocus.FocusWebSurface.Should().BeFalse();
        desiredFocus.YieldManagedFocus.Should().BeFalse();
    }

    [Test]
    public void Derive_NoWebSurfaceHoldsFocus_LeavesManagedFocusAndReturnsNativeFocusToContent()
    {
        var desiredFocus = FocusDerivation.Derive(webSurfaceHoldsFocus: false, popupHoldsFocus: false);

        desiredFocus.FocusWebSurface.Should().BeFalse();
        desiredFocus.YieldManagedFocus.Should().BeFalse();
    }
}
