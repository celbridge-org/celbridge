using System.Text.Json;
using Celbridge.Automation;
using Celbridge.Server;
using Celbridge.Tools;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for the UITools MCP tool methods and the control query they filter by.
/// </summary>
[TestFixture]
public class UIToolTests
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

    private static readonly ControlInfo SearchField = new(
        "search-field",
        "Search",
        "Edit",
        "TextBox",
        new ControlBounds(60, 4, 200, 32),
        true,
        null,
        "query",
        HasKeyboardFocus: true);

    private IApplicationServiceProvider _services = null!;
    private IAutomationService _automationService = null!;

    [SetUp]
    public void SetUp()
    {
        _services = Substitute.For<IApplicationServiceProvider>();
        _automationService = Substitute.For<IAutomationService>();
        _services.GetRequiredService<IAutomationService>().Returns(_automationService);
    }

    [Test]
    public void Matches_EveryNamedFieldEqual_Matches()
    {
        var query = new ControlQuery("bottom-area-toggle-button", "Toggle Bottom Panel", "Button");

        query.Matches(ToggleButton).Should().BeTrue();
    }

    [Test]
    public void Matches_AnEmptyFieldMatchesAnyValue()
    {
        var query = new ControlQuery(string.Empty, string.Empty, "Button");

        query.Matches(ToggleButton).Should().BeTrue();
    }

    [Test]
    public void Matches_OneFieldDiffers_DoesNotMatch()
    {
        var query = new ControlQuery("bottom-area-toggle-button", string.Empty, "MenuItem");

        query.Matches(ToggleButton).Should().BeFalse();
    }

    [Test]
    public void Matches_ComparesExactly()
    {
        var differentCase = new ControlQuery(string.Empty, "toggle bottom panel", string.Empty);
        var prefix = new ControlQuery("bottom-area", string.Empty, string.Empty);

        differentCase.Matches(ToggleButton).Should().BeFalse();
        prefix.Matches(ToggleButton).Should().BeFalse();
    }

    [Test]
    public void IsEmpty_NoFieldNamed_IsEmpty()
    {
        new ControlQuery(string.Empty, string.Empty, string.Empty).IsEmpty.Should().BeTrue();
        new ControlQuery(string.Empty, "OK", string.Empty).IsEmpty.Should().BeFalse();
    }

    [Test]
    public async Task FindControls_ReturnsOnlyTheControlsThatMatch()
    {
        var controls = new List<ControlInfo>
        {
            ToggleButton,
            SearchField
        };
        var snapshot = new ControlSnapshot(controls, 1920, 948, 2);
        _automationService.GetControlsAsync().Returns(Task.FromResult<Result<ControlSnapshot>>(snapshot));

        var tools = new UITools(_services);
        var root = ParseResult(await tools.FindControls(controlType: "Edit"));

        var found = root.GetProperty("controls");
        found.GetArrayLength().Should().Be(1);
        found[0].GetProperty("automationId").GetString().Should().Be("search-field");
        found[0].GetProperty("value").GetString().Should().Be("query");
        found[0].GetProperty("hasKeyboardFocus").GetBoolean().Should().BeTrue();
        root.GetProperty("contentWidth").GetDouble().Should().Be(1920);
        root.GetProperty("rasterizationScale").GetDouble().Should().Be(2);
    }

    [Test]
    public async Task FindControls_EmptyQuery_FailsWithoutReadingTheControls()
    {
        var tools = new UITools(_services);
        var result = await tools.FindControls();

        result.IsError.Should().BeTrue();
        await _automationService.DidNotReceive().GetControlsAsync();
    }

    [Test]
    public async Task InvokeControl_PassesAMatchForTheQuery()
    {
        Func<ControlInfo, bool>? passedMatch = null;
        var invocation = new ControlInvocation(ToggleButton, ControlAction.Invoke);
        _automationService
            .InvokeControlAsync(Arg.Do<Func<ControlInfo, bool>>(match => passedMatch = match))
            .Returns(Task.FromResult<Result<ControlInvocation>>(invocation));

        var tools = new UITools(_services);
        var root = ParseResult(await tools.InvokeControl(automationId: "bottom-area-toggle-button"));

        root.GetProperty("action").GetString().Should().Be("Invoke");
        passedMatch.Should().NotBeNull();
        passedMatch!(ToggleButton).Should().BeTrue();
        passedMatch(SearchField).Should().BeFalse();
    }

    [Test]
    public async Task InvokeControl_EmptyQuery_FailsWithoutInvoking()
    {
        var tools = new UITools(_services);
        var result = await tools.InvokeControl();

        result.IsError.Should().BeTrue();
        await _automationService.DidNotReceiveWithAnyArgs().InvokeControlAsync(default!);
    }

    [Test]
    public async Task FindPageElements_PassesTheSelectorAndFrameToTheService()
    {
        QueryOptions? passedOptions = null;
        var snapshot = new PageElementSnapshot(
            "top",
            0,
            new List<PageElementInfo>(),
            new ControlBounds(300, 100, 800, 600),
            2,
            1920,
            948,
            2);
        _automationService
            .FindPageElementsAsync(Arg.Any<ResourceKey>(), Arg.Do<QueryOptions>(options => passedOptions = options))
            .Returns(Task.FromResult<Result<PageElementSnapshot>>(snapshot));

        var tools = new UITools(_services);
        var root = ParseResult(await tools.FindPageElements("docs/page.html", selector: "#run", frame: "top"));

        passedOptions.Should().NotBeNull();
        passedOptions!.Mode.Should().Be(new SelectorQuery("#run"));
        passedOptions.Frame.Should().Be("top");
        root.GetProperty("webViewBounds").GetProperty("x").GetDouble().Should().Be(300);
        root.GetProperty("devicePixelRatio").GetDouble().Should().Be(2);
    }

    [Test]
    public async Task FindPageElements_NeedsExactlyOneMode()
    {
        var tools = new UITools(_services);

        var none = await tools.FindPageElements("docs/page.html");
        var two = await tools.FindPageElements("docs/page.html", selector: "#run", text: "Run");

        none.IsError.Should().BeTrue();
        two.IsError.Should().BeTrue();
        await _automationService.DidNotReceiveWithAnyArgs().FindPageElementsAsync(default, default!);
    }

    private static JsonElement ParseResult(CallToolResult result)
    {
        var json = result.Content.OfType<TextContentBlock>().Single().Text;
        return JsonDocument.Parse(json).RootElement;
    }
}
