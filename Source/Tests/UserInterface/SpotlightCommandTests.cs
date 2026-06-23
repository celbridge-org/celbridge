using Celbridge.Messaging;
using Celbridge.UserInterface.Commands;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

[TestFixture]
public class SpotlightCommandTests
{
    private IMessengerService _messengerService = null!;
    private ILayoutService _layoutService = null!;

    [SetUp]
    public void Setup()
    {
        _messengerService = Substitute.For<IMessengerService>();
        _layoutService = Substitute.For<ILayoutService>();
    }

    [Test]
    public async Task EmptyTarget_SendsClearSpotlightMessage()
    {
        var command = new SpotlightCommand(_messengerService, _layoutService);
        command.Target = string.Empty;

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeFalse();
        _messengerService.Received(1).Send(Arg.Any<ClearSpotlightMessage>());
        _messengerService.DidNotReceive().Send(Arg.Any<ShowSpotlightMessage>());
        _layoutService.DidNotReceive().SetRegionVisibility(Arg.Any<LayoutRegion>(), Arg.Any<bool>());
    }

    [Test]
    public async Task SidebarTarget_RevealsRegionAndSendsShowSpotlightMessage()
    {
        var command = new SpotlightCommand(_messengerService, _layoutService);
        command.Target = "landmark.explorer";
        command.Label = "This is the Explorer";
        command.DurationMs = 4000;

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeFalse();
        _layoutService.Received(1).SetRegionVisibility(LayoutRegion.Primary, true);
        _messengerService.Received(1).Send(Arg.Is<ShowSpotlightMessage>(message =>
            message.Target == "landmark.explorer" &&
            message.Label == "This is the Explorer" &&
            message.DurationMs == 4000));
        _messengerService.DidNotReceive().Send(Arg.Any<ClearSpotlightMessage>());
    }

    [Test]
    public async Task DocumentsTarget_DoesNotChangeRegionVisibility()
    {
        var command = new SpotlightCommand(_messengerService, _layoutService);
        command.Target = "landmark.documents";

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeFalse();
        _layoutService.DidNotReceive().SetRegionVisibility(Arg.Any<LayoutRegion>(), Arg.Any<bool>());
        _messengerService.Received(1).Send(Arg.Any<ShowSpotlightMessage>());
    }
}
