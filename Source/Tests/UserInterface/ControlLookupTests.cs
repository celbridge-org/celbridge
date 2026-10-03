using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.UserInterface.Services;

namespace Celbridge.Tests.UserInterface;

[TestFixture]
public class ControlLookupTests
{
    private static readonly ControlInfo ToggleButton = new(
        "bottom-area-toggle-button",
        "Toggle Bottom Panel",
        "Button",
        "Button",
        new ControlBounds(10, 4, 32, 32),
        true,
        null,
        null);

    [Test]
    public void Matches_EveryNamedFieldEqual_Matches()
    {
        var query = new ControlQuery("bottom-area-toggle-button", "Toggle Bottom Panel", "Button");

        ControlQueryMatcher.Matches(query, ToggleButton).Should().BeTrue();
    }

    [Test]
    public void Matches_AnEmptyFieldMatchesAnyValue()
    {
        var query = new ControlQuery(string.Empty, string.Empty, "Button");

        ControlQueryMatcher.Matches(query, ToggleButton).Should().BeTrue();
    }

    [Test]
    public void Matches_OneFieldDiffers_DoesNotMatch()
    {
        var query = new ControlQuery("bottom-area-toggle-button", string.Empty, "MenuItem");

        ControlQueryMatcher.Matches(query, ToggleButton).Should().BeFalse();
    }

    [Test]
    public void Matches_ComparesExactly()
    {
        var differentCase = new ControlQuery(string.Empty, "toggle bottom panel", string.Empty);
        var prefix = new ControlQuery("bottom-area", string.Empty, string.Empty);

        ControlQueryMatcher.Matches(differentCase, ToggleButton).Should().BeFalse();
        ControlQueryMatcher.Matches(prefix, ToggleButton).Should().BeFalse();
    }

    [Test]
    public void IsEmpty_NoFieldNamed_IsEmpty()
    {
        ControlQueryMatcher.IsEmpty(new ControlQuery(string.Empty, string.Empty, string.Empty)).Should().BeTrue();
        ControlQueryMatcher.IsEmpty(new ControlQuery(string.Empty, "OK", string.Empty)).Should().BeFalse();
    }

    [Test]
    public async Task FindControlsAsync_EmptyQuery_FailsWithoutSearching()
    {
        var userInterfaceService = Substitute.For<IUserInterfaceService>();
        var service = new ControlLookupService(userInterfaceService);

        var result = await service.FindControlsAsync(new ControlQuery(string.Empty, string.Empty, string.Empty));

        result.IsFailure.Should().BeTrue();
        _ = userInterfaceService.DidNotReceive().XamlRoot;
    }
}
