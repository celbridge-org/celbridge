using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface.Services;
using Microsoft.UI.Xaml;

namespace Celbridge.Tests.UserInterface;

[TestFixture]
public class SpotlightServiceTests
{
    private ILogger<SpotlightService> _logger = null!;
    private IMessengerService _messengerService = null!;

    [SetUp]
    public void Setup()
    {
        _logger = Substitute.For<ILogger<SpotlightService>>();
        _messengerService = Substitute.For<IMessengerService>();
    }

    [Test]
    public void RegisterPresenter_ThenClear_HidesThePresenter()
    {
        var service = new SpotlightService(_logger, _messengerService);
        var presenter = new StubSpotlightPresenter();

        service.RegisterPresenter(presenter);
        service.ClearSpotlight();

        presenter.HideCount.Should().Be(1);
    }

    [Test]
    public void RegisterPresenter_ReplacesPreviousPresenter()
    {
        var service = new SpotlightService(_logger, _messengerService);
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
        var service = new SpotlightService(_logger, _messengerService);
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
        var service = new SpotlightService(_logger, _messengerService);
        var presenter = new StubSpotlightPresenter();

        service.RegisterPresenter(presenter);
        service.UnregisterPresenter(presenter);
        service.ClearSpotlight();

        // No presenter is registered, so clearing drives nothing.
        presenter.HideCount.Should().Be(0);
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
