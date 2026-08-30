using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

[TestFixture]
public class SpotlightServiceTests
{
    private ILogger<SpotlightService> _logger = null!;
    private IMessengerService _messengerService = null!;
    private ILayoutService _layoutService = null!;
    private IWindowModeService _windowModeService = null!;
    private ISpotlightRegistry _landmarkRegistry = null!;

    [SetUp]
    public void Setup()
    {
        _logger = Substitute.For<ILogger<SpotlightService>>();
        _messengerService = Substitute.For<IMessengerService>();
        _layoutService = Substitute.For<ILayoutService>();
        _windowModeService = Substitute.For<IWindowModeService>();
        _landmarkRegistry = Substitute.For<ISpotlightRegistry>();
    }

    [Test]
    public void RegisterPresenter_ThenClear_HidesThePresenter()
    {
        var service = new SpotlightService(_logger, _messengerService, _layoutService, _windowModeService, _landmarkRegistry);
        var presenter = new StubSpotlightPresenter();

        service.RegisterPresenter(presenter);
        service.ClearSpotlight();

        presenter.HideCount.Should().Be(1);
    }

    [Test]
    public void RegisterPresenter_ReplacesPreviousPresenter()
    {
        var service = new SpotlightService(_logger, _messengerService, _layoutService, _windowModeService, _landmarkRegistry);
        var first = new StubSpotlightPresenter();
        var second = new StubSpotlightPresenter();

        service.RegisterPresenter(first);
        service.RegisterPresenter(second);
        service.ClearSpotlight();

        // Only the current presenter is driven; the replaced one is left alone.
        second.HideCount.Should().Be(1);
        first.HideCount.Should().Be(0);
    }

    [Test]
    public void UnregisterPresenter_NotCurrent_IsIgnored()
    {
        var service = new SpotlightService(_logger, _messengerService, _layoutService, _windowModeService, _landmarkRegistry);
        var first = new StubSpotlightPresenter();
        var second = new StubSpotlightPresenter();

        service.RegisterPresenter(first);
        service.RegisterPresenter(second);
        service.UnregisterPresenter(first);
        service.ClearSpotlight();

        // The stale unregister did nothing, so the second presenter is still current.
        second.HideCount.Should().Be(1);
    }

    [Test]
    public void UnregisterPresenter_Current_ClearsTheSlot()
    {
        var service = new SpotlightService(_logger, _messengerService, _layoutService, _windowModeService, _landmarkRegistry);
        var presenter = new StubSpotlightPresenter();

        service.RegisterPresenter(presenter);
        service.UnregisterPresenter(presenter);
        service.ClearSpotlight();

        // No presenter is registered, so clearing drives nothing.
        presenter.HideCount.Should().Be(0);
    }

    [Test]
    public async Task ShowSpotlightAsync_InPresentationMode_IsRefused()
    {
        // A landmark that gates on an area, so without the Presentation guard the reveal below would
        // fire and take the user out of the mode.
        _landmarkRegistry
            .TryGetLandmark("search-input", out Arg.Any<LandmarkDescriptor?>())
            .Returns(call =>
            {
                call[1] = new LandmarkDescriptor("search-input", WorkspaceArea.Utility);
                return true;
            });
        _windowModeService.LayoutMode.Returns(LayoutMode.Presentation);

        var service = new SpotlightService(_logger, _messengerService, _layoutService, _windowModeService, _landmarkRegistry);
        service.RegisterPresenter(new StubSpotlightPresenter());

        var result = await service.ShowSpotlightAsync("search-input", "Search", 0);

        // Revealing the landmark's area would drop the user out of their presentation, which is theirs
        // to leave.
        result.IsFailure.Should().BeTrue();
        _layoutService.DidNotReceiveWithAnyArgs().SetAreaVisibility(default, default);
    }

    private sealed class StubSpotlightPresenter : ISpotlightPresenter
    {
        public int HideCount { get; private set; }

        public FrameworkElement? ResolveLandmark(string landmarkId)
        {
            return null;
        }

        public void ShowSpotlight(FrameworkElement target, string label)
        {
        }

        public void HideSpotlight()
        {
            HideCount++;
        }

        public event EventHandler? SpotlightClosed
        {
            add { }
            remove { }
        }
    }
}
