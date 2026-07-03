using Celbridge.UserInterface.Commands;
using Celbridge.UserInterface.Services;

namespace Celbridge.Tests.UserInterface;

[TestFixture]
public class SpotlightCommandTests
{
    private ISpotlightService _spotlightService = null!;

    [SetUp]
    public void Setup()
    {
        _spotlightService = Substitute.For<ISpotlightService>();
    }

    [Test]
    public async Task EmptyTarget_ClearsSpotlight()
    {
        var command = new SpotlightCommand(_spotlightService);
        command.Target = string.Empty;

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeFalse();
        _spotlightService.Received(1).ClearSpotlight();
        await _spotlightService.DidNotReceive().ShowSpotlightAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>());
    }

    [Test]
    public async Task Target_ShowsSpotlight()
    {
        _spotlightService.ShowSpotlightAsync("explorer-panel", "This is the Explorer", 4000)
            .Returns(Result.Ok());

        var command = new SpotlightCommand(_spotlightService);
        command.Target = "explorer-panel";
        command.Label = "This is the Explorer";
        command.DurationMs = 4000;

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeFalse();
        await _spotlightService.Received(1).ShowSpotlightAsync("explorer-panel", "This is the Explorer", 4000);
        _spotlightService.DidNotReceive().ClearSpotlight();
    }

    [Test]
    public async Task Target_PropagatesShowFailure()
    {
        _spotlightService.ShowSpotlightAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns(Result.Fail("The control is not currently on screen."));

        var command = new SpotlightCommand(_spotlightService);
        command.Target = "new-file-button";

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
    }
}
