using Celbridge.Messaging;
using Celbridge.Tests.Helpers;
using Celbridge.WebHost;
using Celbridge.WebHost.Platform;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Unit tests for the web view focus registry. A test cannot create a WebView2 control, so these views have
/// none and the tests cover the focus model only.
/// </summary>
[TestFixture]
public class WebViewFocusRegistryTests
{
    private IServiceProvider? _previousServiceProvider;
    private IFocusService _focusService = null!;
    private IFocusReconciler _focusReconciler = null!;
    private List<FocusClaim> _claims = null!;
    private WebViewFocusRegistry _registry = null!;

    [SetUp]
    public void SetUp()
    {
        // The registry finds the reconciler through the service locator. The previous provider is restored in
        // TearDown, since other fixtures share the locator.
        _previousServiceProvider = ServiceLocator.ServiceProvider;

        _focusReconciler = Substitute.For<IFocusReconciler>();

        var services = new ServiceCollection();
        services.AddSingleton(_focusReconciler);
        ServiceLocator.Initialize(services.BuildServiceProvider());

        _focusService = Substitute.For<IFocusService>();
        _claims = new List<FocusClaim>();
        _focusService
            .When(service => service.OnFocusReceived(Arg.Any<FocusClaim>()))
            .Do(call => _claims.Add(call.Arg<FocusClaim>()));

        _registry = new WebViewFocusRegistry(
            _focusService,
            new NullWebViewFocusMonitor(),
            Substitute.For<IMessengerService>(),
            new StubMessageDispatcher(),
            new NullLogger<WebViewFocusRegistry>());
    }

    [TearDown]
    public void TearDown()
    {
        if (_previousServiceProvider is not null)
        {
            ServiceLocator.Initialize(_previousServiceProvider);
        }
        else
        {
            ServiceLocator.Reset();
        }
    }

    [Test]
    public void GrantFocus_RegisteredView_ReportsItsFocusContext()
    {
        var view = new FakeWebView("notes.md");
        var focusContext = new TestFocusContext(FocusPanelId.Documents);
        _registry.Register(view, focusContext.Create());

        _registry.GrantFocus(view);

        _claims.Should().ContainSingle();
        var claim = _claims[0];
        claim.Kind.Should().Be(FocusClaimKind.WebSurface);
        claim.Panel.Should().Be(FocusPanelId.Documents);
        claim.EditTarget.Should().BeSameAs(focusContext.EditTarget);
        claim.Surface!.SurfaceName.Should().Be(view.Resource.ToString());

        _registry.IsFocusedSurface(view).Should().BeTrue();
        _registry.HasFocusedSurface.Should().BeTrue();
        focusContext.FocusGainedCount.Should().Be(1);
        focusContext.DomGrantCount.Should().Be(1);
        _focusReconciler.Received(1).Reconcile();
    }

    [Test]
    public void ReleasingTheClaim_RunsTheViewsReleaseAndClearsItsFocus()
    {
        var view = new FakeWebView("notes.md");
        var focusContext = new TestFocusContext(FocusPanelId.Documents);
        _registry.Register(view, focusContext.Create());
        _registry.GrantFocus(view);

        // The focus service runs the release when another surface or panel claims the keyboard.
        _claims[0].ReleaseFocus!.Invoke();

        focusContext.ReleaseCount.Should().Be(1);
        _registry.IsFocusedSurface(view).Should().BeFalse();
        _registry.HasFocusedSurface.Should().BeFalse();
    }

    [Test]
    public void ClosingView_DropsItsRegistrationAndEditTarget()
    {
        var view = new FakeWebView("notes.md");
        var focusContext = new TestFocusContext(FocusPanelId.Documents);
        _registry.Register(view, focusContext.Create());
        _registry.GrantFocus(view);

        view.Close();

        _registry.IsFocusedSurface(view).Should().BeFalse();
        _registry.HasFocusedSurface.Should().BeFalse();
        _focusService.Received(1).ClearEditTarget(focusContext.EditTarget);

        // A grant to a closed view finds no registration, so nothing is reported.
        _registry.GrantFocus(view);
        _claims.Should().ContainSingle();
    }

    [Test]
    public void RegisteringAgain_MakesANewSurfaceAndKeepsTheKeyboard()
    {
        var view = new FakeWebView("utils:example.utility");
        var panelFocusContext = new TestFocusContext(FocusPanelId.CustomUtility);
        var documentFocusContext = new TestFocusContext(FocusPanelId.Documents);
        _registry.Register(view, panelFocusContext.Create());
        _registry.GrantFocus(view);

        // A redock registers the live view under the focus context of the area it moved to.
        _registry.Register(view, documentFocusContext.Create());

        _claims.Should().HaveCount(2);
        _claims[1].Panel.Should().Be(FocusPanelId.Documents);
        _claims[1].EditTarget.Should().BeSameAs(documentFocusContext.EditTarget);
        _claims[1].Surface.Should().NotBeSameAs(_claims[0].Surface);
        documentFocusContext.DomGrantCount.Should().Be(1);
        _registry.IsFocusedSurface(view).Should().BeTrue();

        // The registry observes the view once, however many times it registers.
        view.ClosingSubscriberCount.Should().Be(1);
    }

    [Test]
    public void GrantBeforeRegistration_AppliesWhenTheViewRegisters()
    {
        var view = new FakeWebView("notes.md");
        var focusContext = new TestFocusContext(FocusPanelId.Documents);
        _focusService.FocusedPanel.Returns(FocusPanelId.Documents);

        _registry.GrantFocus(view);
        _claims.Should().BeEmpty();

        _registry.Register(view, focusContext.Create());

        _claims.Should().ContainSingle();
        _registry.IsFocusedSurface(view).Should().BeTrue();
    }

    [Test]
    public void GrantBeforeRegistration_IsDroppedWhenFocusMovedToAnotherPanel()
    {
        var view = new FakeWebView("notes.md");
        var focusContext = new TestFocusContext(FocusPanelId.Documents);
        _focusService.FocusedPanel.Returns(FocusPanelId.Explorer);

        _registry.GrantFocus(view);
        _registry.Register(view, focusContext.Create());

        _claims.Should().BeEmpty();
        _registry.IsFocusedSurface(view).Should().BeFalse();
    }

    [Test]
    public void GrantBeforeRegistration_IsSupersededByALaterGrant()
    {
        var firstView = new FakeWebView("first.md");
        var secondView = new FakeWebView("second.md");
        _focusService.FocusedPanel.Returns(FocusPanelId.Documents);

        _registry.GrantFocus(firstView);
        _registry.GrantFocus(secondView);

        _registry.Register(firstView, new TestFocusContext(FocusPanelId.Documents).Create());
        _claims.Should().BeEmpty();

        _registry.Register(secondView, new TestFocusContext(FocusPanelId.Documents).Create());
        _claims.Should().ContainSingle();
        _registry.IsFocusedSurface(secondView).Should().BeTrue();
    }

    // Counts what the registry does with a focus context's callbacks.
    private sealed class TestFocusContext
    {
        private readonly FocusPanelId _panel;

        public TestFocusContext(FocusPanelId panel)
        {
            _panel = panel;
        }

        public IEditTarget EditTarget { get; } = Substitute.For<IEditTarget>();

        public int ReleaseCount { get; private set; }

        public int DomGrantCount { get; private set; }

        public int FocusGainedCount { get; private set; }

        public WebViewFocusContext Create()
        {
            return new WebViewFocusContext(
                _panel,
                EditTarget,
                ReleaseFocus: () => ReleaseCount++,
                GrantDomFocus: () =>
                {
                    DomGrantCount++;
                    return Task.CompletedTask;
                },
                OnFocusGained: () => FocusGainedCount++);
        }
    }

    // The dispatcher is internal, so it is stubbed by hand. A view with no control never attaches.
    private sealed class StubMessageDispatcher : IWebSurfaceMessageDispatcher
    {
        public void AddHandler(string method, Action<WebSurfaceMessage> handler)
        {
        }

        public void Attach(IWebView view)
        {
        }

        public void Detach(IWebView view)
        {
        }
    }
}
