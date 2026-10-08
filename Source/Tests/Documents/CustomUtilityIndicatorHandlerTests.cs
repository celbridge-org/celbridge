using Celbridge.Documents;
using Celbridge.Documents.Views;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Workspace;
using StreamJsonRpc;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Tests for the bridge between a utility editor's page and its rail button indicator.
/// </summary>
[TestFixture]
public class CustomUtilityIndicatorHandlerTests
{
    private static readonly EditorId UtilityId = EditorId.Create("filechange-history", "logger");

    private IMessengerService _messengerService = null!;
    private List<UtilityIndicatorChangedMessage> _messages = null!;

    [SetUp]
    public void Setup()
    {
        _messengerService = new MessengerService();
        _messages = new List<UtilityIndicatorChangedMessage>();
        _messengerService.Register<UtilityIndicatorChangedMessage>(this, (_, message) => _messages.Add(message));
    }

    [TearDown]
    public void TearDown()
    {
        _messengerService.UnregisterAll(this);
    }

    [Test]
    public void ATone_IsSentForTheUtilitysRailButton()
    {
        var handler = new CustomUtilityIndicatorHandler(_messengerService, UtilityId, isUtility: true);

        handler.SetIndicator("Danger", "  Recording ");

        _messages.Should().Equal(new UtilityIndicatorChangedMessage(UtilityId, UtilityIndicatorTone.Danger, "Recording"));
    }

    [Test]
    public void AnIcon_IsSentWithTheTone_AndDroppedWhenItClears()
    {
        var handler = new CustomUtilityIndicatorHandler(_messengerService, UtilityId, isUtility: true);

        handler.SetIndicator("danger", "Recording", "bs-record-circle-fill");
        handler.SetIndicator("none", "", "bs-record-circle-fill");

        _messages[0].IconName.Should().Be("bs-record-circle-fill");
        _messages[1].IconName.Should().BeEmpty();
    }

    [TestCase("record")]
    [TestCase("bs-")]
    [TestCase("bs record")]
    public void AnIconThatIsNotAPrefixedName_IsRefused(string icon)
    {
        var handler = new CustomUtilityIndicatorHandler(_messengerService, UtilityId, isUtility: true);

        var act = () => handler.SetIndicator("danger", "", icon);

        act.Should().Throw<LocalRpcException>();
    }

    [Test]
    public void RepeatingTheSameIndicator_SendsNothingNew()
    {
        var handler = new CustomUtilityIndicatorHandler(_messengerService, UtilityId, isUtility: true);

        handler.SetIndicator("danger", "Recording");
        handler.SetIndicator("danger", "Recording");

        _messages.Should().HaveCount(1);
    }

    [Test]
    public void NoneClearsTheIndicator_AndDropsTheLabel()
    {
        var handler = new CustomUtilityIndicatorHandler(_messengerService, UtilityId, isUtility: true);

        handler.SetIndicator("danger", "Recording");
        handler.SetIndicator("none", "Recording");

        _messages.Last().Should().Be(new UtilityIndicatorChangedMessage(UtilityId, UtilityIndicatorTone.None, string.Empty));
    }

    [Test]
    public void Reset_ClearsAnIndicatorThatIsOn_AndIsQuietOtherwise()
    {
        var handler = new CustomUtilityIndicatorHandler(_messengerService, UtilityId, isUtility: true);

        handler.Reset();
        _messages.Should().BeEmpty();

        handler.SetIndicator("caution");
        handler.Reset();

        _messages.Last().Tone.Should().Be(UtilityIndicatorTone.None);
    }

    [Test]
    public void AnUnknownTone_IsRefused()
    {
        var handler = new CustomUtilityIndicatorHandler(_messengerService, UtilityId, isUtility: true);

        var act = () => handler.SetIndicator("purple");

        act.Should().Throw<LocalRpcException>();
        _messages.Should().BeEmpty();
    }

    [Test]
    public void ADocumentEditor_HasNoRailButtonToMark()
    {
        var handler = new CustomUtilityIndicatorHandler(_messengerService, UtilityId, isUtility: false);

        var act = () => handler.SetIndicator("danger");

        act.Should().Throw<LocalRpcException>();
        _messages.Should().BeEmpty();
    }

    [TestCase(null, UtilityIndicatorTone.None)]
    [TestCase("", UtilityIndicatorTone.None)]
    [TestCase("SUCCESS", UtilityIndicatorTone.Success)]
    [TestCase("accent", UtilityIndicatorTone.Accent)]
    public void ToneNames_ParseIgnoringCase(string? name, UtilityIndicatorTone expected)
    {
        UtilityIndicatorTones.TryParse(name, out var tone).Should().BeTrue();
        tone.Should().Be(expected);
    }

    [Test]
    public void ANumericToneName_IsNotATone()
    {
        UtilityIndicatorTones.TryParse("7", out _).Should().BeFalse();
    }
}
