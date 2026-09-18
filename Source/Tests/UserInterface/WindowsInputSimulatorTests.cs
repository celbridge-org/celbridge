using Celbridge.UserInterface.Platform;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the Windows key simulator: the key messages it plans for a press, which hold on any
/// platform, and its refusal to post while no window holds the keyboard, which needs Windows.
/// </summary>
[TestFixture]
public class WindowsInputSimulatorTests
{
    private const uint MessageKeyDown = 0x0100;
    private const uint MessageKeyUp = 0x0101;
    private const ushort VirtualKeyEscape = 0x1B;
    private const ushort VirtualKeyLeft = 0x25;

    [Test]
    public void PlanKeyMessages_Escape_IsAKeyDownThenAKeyUp()
    {
        var result = WindowsInputSimulator.PlanKeyMessages("Escape", command: false, control: false, shift: false, option: false);

        // A repeat count of one and scan code 0x01, with the release also flagging the key as previously down
        // and now up.
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(
            new KeyMessage(MessageKeyDown, VirtualKeyEscape, 0x00010001),
            new KeyMessage(MessageKeyUp, VirtualKeyEscape, 0xC0010001));
    }

    [Test]
    public void PlanKeyMessages_NavigationKey_CarriesTheExtendedFlag()
    {
        // The extended flag is what tells these keys apart from their numeric keypad twins.
        var result = WindowsInputSimulator.PlanKeyMessages("Left", command: false, control: false, shift: false, option: false);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(
            new KeyMessage(MessageKeyDown, VirtualKeyLeft, 0x014B0001),
            new KeyMessage(MessageKeyUp, VirtualKeyLeft, 0xC14B0001));
    }

    [Test]
    public void PlanKeyMessages_KeyName_IgnoresCase()
    {
        var result = WindowsInputSimulator.PlanKeyMessages("escape", command: false, control: false, shift: false, option: false);

        result.IsSuccess.Should().BeTrue();
    }

    [TestCase(true, false, false, false)]
    [TestCase(false, true, false, false)]
    [TestCase(false, false, true, false)]
    [TestCase(false, false, false, true)]
    public void PlanKeyMessages_WithAModifier_IsRefused(bool command, bool control, bool shift, bool option)
    {
        var result = WindowsInputSimulator.PlanKeyMessages("Tab", command, control, shift, option);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Modifiers are not supported on Windows");
    }

    [Test]
    public void PlanKeyMessages_UnknownKey_ListsTheSupportedKeys()
    {
        var result = WindowsInputSimulator.PlanKeyMessages("A", command: false, control: false, shift: false, option: false);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Escape");
    }

    [Test]
    public void SupportedKeys_MatchTheMacOSSimulator()
    {
        // The tool's guide lists one set of key names for both platforms.
        WindowsInputSimulator.SupportedKeys.Should().Equal(MacOSInputSimulator.SupportedKeys);
    }

    [Test]
    [Platform("Win")]
    public void PressKey_OnAThreadWithNoFocusedWindow_IsRefused()
    {
        // The test thread owns no window, so nothing holds its keyboard and nothing is posted.
        var result = WindowsInputSimulator.PressKey("Escape", command: false, control: false, shift: false, option: false);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("no focused window");
    }
}
