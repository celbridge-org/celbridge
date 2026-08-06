using Celbridge.UserInterface.Services;
using Celbridge.WebHost;

namespace Celbridge.Tests.UserInterface;

[TestFixture]
public class FocusReconcilerTests
{
    private IWebViewFocusRegistry _webViewFocusRegistry = null!;
    private IManagedFocusSink _managedFocusSink = null!;
    private IHostWindowFocus _hostWindowFocus = null!;
    private FocusReconciler _focusReconciler = null!;

    [SetUp]
    public void SetUp()
    {
        _webViewFocusRegistry = Substitute.For<IWebViewFocusRegistry>();
        _managedFocusSink = Substitute.For<IManagedFocusSink>();
        _hostWindowFocus = Substitute.For<IHostWindowFocus>();
        _focusReconciler = new FocusReconciler(_webViewFocusRegistry, _managedFocusSink, _hostWindowFocus);
    }

    [Test]
    public void Reconcile_WithFocusedSurface_ParksManagedFocusBeforeFocusingTheSurface()
    {
        _webViewFocusRegistry.HasFocusedSurface.Returns(true);

        _focusReconciler.Reconcile();

        // Managed focus must park before the native apply: applying managed focus resigns the native
        // first responder, so the reverse order would undo the focus being established.
        Received.InOrder(() =>
        {
            _managedFocusSink.TakeFocus();
            _webViewFocusRegistry.FocusFocusedSurface();
        });
        _hostWindowFocus.DidNotReceive().FocusHostWindow();
    }

    [Test]
    public void Reconcile_WithoutFocusedSurface_ReturnsNativeFocusToTheHostWindow()
    {
        _webViewFocusRegistry.HasFocusedSurface.Returns(false);

        _focusReconciler.Reconcile();

        _hostWindowFocus.Received(1).FocusHostWindow();
        _managedFocusSink.DidNotReceive().TakeFocus();
        _webViewFocusRegistry.DidNotReceive().FocusFocusedSurface();
    }

    [Test]
    public void Reconcile_Repeated_ReappliesTheSameState()
    {
        _webViewFocusRegistry.HasFocusedSurface.Returns(true);

        _focusReconciler.Reconcile();
        _focusReconciler.Reconcile();

        _managedFocusSink.Received(2).TakeFocus();
        _webViewFocusRegistry.Received(2).FocusFocusedSurface();
        _hostWindowFocus.DidNotReceive().FocusHostWindow();
    }
}
