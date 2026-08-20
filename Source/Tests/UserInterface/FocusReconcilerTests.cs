using Celbridge.UserInterface.Services;
using Celbridge.WebHost;

namespace Celbridge.Tests.UserInterface;

[TestFixture]
public class FocusReconcilerTests
{
    private IWebViewFocusRegistry _webViewFocusRegistry = null!;
    private IManagedFocus _managedFocus = null!;
    private IHostWindowFocus _hostWindowFocus = null!;
    private ILogger<FocusReconciler> _logger = null!;
    private FocusReconciler _focusReconciler = null!;

    [SetUp]
    public void SetUp()
    {
        _webViewFocusRegistry = Substitute.For<IWebViewFocusRegistry>();
        _managedFocus = Substitute.For<IManagedFocus>();
        _hostWindowFocus = Substitute.For<IHostWindowFocus>();
        _logger = Substitute.For<ILogger<FocusReconciler>>();
        _focusReconciler = new FocusReconciler(_webViewFocusRegistry, _managedFocus, _hostWindowFocus, _logger);
    }

    [Test]
    public void Reconcile_WithPopupHoldingFocus_LeavesTheKeyboardWithThePopup()
    {
        _webViewFocusRegistry.HasFocusedSurface.Returns(true);
        _managedFocus.IsPopupHoldingFocus.Returns(true);

        _focusReconciler.Reconcile();

        // Yielding managed focus would pull it out of the open popup, which then stops receiving input
        // while still on screen.
        _managedFocus.DidNotReceive().Yield();
        _webViewFocusRegistry.DidNotReceive().FocusFocusedSurface();
    }

    [Test]
    public void Reconcile_WithFocusedSurface_YieldsManagedFocusBeforeFocusingTheSurface()
    {
        _webViewFocusRegistry.HasFocusedSurface.Returns(true);

        _focusReconciler.Reconcile();

        // Managed focus must yield before the native apply: applying managed focus resigns the native
        // first responder, so the reverse order would undo the focus being established.
        Received.InOrder(() =>
        {
            _managedFocus.Yield();
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
        _managedFocus.DidNotReceive().Yield();
        _webViewFocusRegistry.DidNotReceive().FocusFocusedSurface();
    }

    [Test]
    public void Reconcile_Repeated_ReappliesTheSameState()
    {
        _webViewFocusRegistry.HasFocusedSurface.Returns(true);

        _focusReconciler.Reconcile();
        _focusReconciler.Reconcile();

        _managedFocus.Received(2).Yield();
        _webViewFocusRegistry.Received(2).FocusFocusedSurface();
        _hostWindowFocus.DidNotReceive().FocusHostWindow();
    }
}
