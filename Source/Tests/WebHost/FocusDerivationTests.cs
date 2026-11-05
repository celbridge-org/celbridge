using Celbridge.UserInterface;
using Celbridge.WebHost;

namespace Celbridge.Tests.WebHost;

[TestFixture]
public class FocusDerivationTests
{
    [Test]
    public void Derive_WebSurfaceHoldsFocus_FocusesSurfaceAndYieldsManagedFocus()
    {
        var desiredFocus = FocusDerivation.Derive(
            webSurfaceHoldsFocus: true,
            FocusLocation.MainContent,
            hostedWebViewFocusIsNative: true);

        desiredFocus.FocusWebSurface.Should().BeTrue();
        desiredFocus.YieldManagedFocus.Should().BeTrue();
    }

    [Test]
    public void Derive_PopupHoldsFocus_LeavesFocusWithThePopup()
    {
        // A popup reports no panel, so the model still names the surface underneath it. Yielding managed
        // focus would pull it out of the popup, which then stops receiving input while still on screen.
        var desiredFocus = FocusDerivation.Derive(
            webSurfaceHoldsFocus: true,
            FocusLocation.Popup,
            hostedWebViewFocusIsNative: true);

        desiredFocus.FocusWebSurface.Should().BeFalse();
        desiredFocus.YieldManagedFocus.Should().BeFalse();
    }

    [Test]
    public void Derive_NoWebSurfaceHoldsFocus_LeavesManagedFocusAndReturnsNativeFocusToContent()
    {
        var desiredFocus = FocusDerivation.Derive(
            webSurfaceHoldsFocus: false,
            FocusLocation.MainContent,
            hostedWebViewFocusIsNative: true);

        desiredFocus.FocusWebSurface.Should().BeFalse();
        desiredFocus.YieldManagedFocus.Should().BeFalse();
    }

    [Test]
    public void Derive_ManagedFocusIsDetached_YieldsManagedFocus()
    {
        // A dismissed popup leaves managed focus on the item that was focused inside it, so the keys the
        // user types next reach a menu that is no longer on screen.
        var desiredFocus = FocusDerivation.Derive(
            webSurfaceHoldsFocus: false,
            FocusLocation.Detached,
            hostedWebViewFocusIsNative: true);

        desiredFocus.FocusWebSurface.Should().BeFalse();
        desiredFocus.YieldManagedFocus.Should().BeTrue();
    }

    [Test]
    public void Derive_WebViewFocusIsManaged_NeverYieldsManagedFocus()
    {
        // On this head, focusing a web view moves managed focus to the web view. A yield would take the keyboard
        // off the focused surface's web view.
        var surfaceFocus = FocusDerivation.Derive(
            webSurfaceHoldsFocus: true,
            FocusLocation.MainContent,
            hostedWebViewFocusIsNative: false);
        var detachedFocus = FocusDerivation.Derive(
            webSurfaceHoldsFocus: false,
            FocusLocation.Detached,
            hostedWebViewFocusIsNative: false);

        surfaceFocus.FocusWebSurface.Should().BeTrue();
        surfaceFocus.YieldManagedFocus.Should().BeFalse();
        detachedFocus.FocusWebSurface.Should().BeFalse();
        detachedFocus.YieldManagedFocus.Should().BeFalse();
    }
}
