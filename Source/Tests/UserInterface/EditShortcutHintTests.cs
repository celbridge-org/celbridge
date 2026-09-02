using Celbridge.Platform;
using Celbridge.UserInterface.Helpers;
using Celbridge.UserInterface.Platform;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the edit shortcut hints shown beside menu item labels. The hint is a pure function of the
/// verb and the platform capabilities, so these run on every platform.
/// </summary>
[TestFixture]
public class EditShortcutHintTests
{
    private static IPlatformInfo CreatePlatformInfo(
        CommandModifierKey commandModifier,
        bool treatsCtrlYAsRedo = false,
        bool treatsBackspaceAsDeleteKey = false)
    {
        var platformInfo = Substitute.For<IPlatformInfo>();
        platformInfo.CommandModifier.Returns(commandModifier);
        platformInfo.TreatsCtrlYAsRedo.Returns(treatsCtrlYAsRedo);
        platformInfo.TreatsBackspaceAsDeleteKey.Returns(treatsBackspaceAsDeleteKey);

        return platformInfo;
    }

    private static IPlatformInfo MacOS => CreatePlatformInfo(
        CommandModifierKey.Command,
        treatsBackspaceAsDeleteKey: true);

    private static IPlatformInfo Windows => CreatePlatformInfo(
        CommandModifierKey.Control,
        treatsCtrlYAsRedo: true);

    private static IPlatformInfo Linux => CreatePlatformInfo(CommandModifierKey.Control);

    [TestCase(EditIntent.Undo, "⌘Z")]
    [TestCase(EditIntent.Redo, "⇧⌘Z")]
    [TestCase(EditIntent.Cut, "⌘X")]
    [TestCase(EditIntent.Copy, "⌘C")]
    [TestCase(EditIntent.Paste, "⌘V")]
    [TestCase(EditIntent.SelectAll, "⌘A")]
    [TestCase(EditIntent.Duplicate, "⌘D")]
    [TestCase(EditIntent.Delete, "⌫")]
    [TestCase(EditIntent.Rename, "F2")]
    public void For_OnMacOS_NamesTheChordInGlyphs(EditIntent intent, string expected)
    {
        EditShortcutHint.For(intent, MacOS).Should().Be(expected);
    }

    [TestCase(EditIntent.Undo, "Ctrl+Z")]
    [TestCase(EditIntent.Redo, "Ctrl+Y")]
    [TestCase(EditIntent.Cut, "Ctrl+X")]
    [TestCase(EditIntent.Copy, "Ctrl+C")]
    [TestCase(EditIntent.Paste, "Ctrl+V")]
    [TestCase(EditIntent.SelectAll, "Ctrl+A")]
    [TestCase(EditIntent.Duplicate, "Ctrl+D")]
    [TestCase(EditIntent.Delete, "Del")]
    [TestCase(EditIntent.Rename, "F2")]
    public void For_OnWindows_NamesTheChordInWords(EditIntent intent, string expected)
    {
        EditShortcutHint.For(intent, Windows).Should().Be(expected);
    }

    [Test]
    public void For_WhereCtrlYIsNotRedo_NamesTheCrossPlatformRedoChord()
    {
        EditShortcutHint.For(EditIntent.Redo, Linux).Should().Be("Ctrl+Shift+Z");
    }

    [Test]
    public void For_EveryEditVerb_NamesAChord()
    {
        // A verb with no hint would appear in a menu with a blank shortcut column, so every verb the app
        // offers has to name one.
        foreach (var intent in Enum.GetValues<EditIntent>())
        {
            EditShortcutHint.For(intent, MacOS).Should().NotBeNullOrWhiteSpace();
            EditShortcutHint.For(intent, Windows).Should().NotBeNullOrWhiteSpace();
        }
    }

    [Test]
    public void For_OnMacOS_MatchesTheChordsTheMenuBarHandles()
    {
        // The hint is display only, so it is worth nothing unless it names the chord the macOS menu bar
        // actually carries for that verb.
        foreach (var shortcut in MacOSEditShortcuts.All)
        {
            var expected = (shortcut.Shift ? "⇧⌘" : "⌘") + char.ToUpperInvariant(shortcut.Character);

            EditShortcutHint.For(shortcut.Intent, MacOS).Should().Be(expected);
        }
    }
}
