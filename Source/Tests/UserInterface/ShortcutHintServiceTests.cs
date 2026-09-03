using Celbridge.Platform;
using Celbridge.Tests.Localization;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.Platform;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the shortcut hints shown beside menu item labels. They read the application's own en-US
/// resources, so a hint with no entry fails here rather than reaching a user as a raw resource name.
/// </summary>
[TestFixture]
public class ShortcutHintServiceTests
{
    private static IStringLocalizer CreateStringLocalizer()
    {
        var strings = TestLocalizerService.LoadStrings();

        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer[Arg.Any<string>()].Returns(call =>
        {
            var name = call.Arg<string>();
            var found = strings.TryGetValue(name, out var value);

            return new LocalizedString(name, found ? value! : name, resourceNotFound: !found);
        });

        return stringLocalizer;
    }

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

    private static ShortcutHintService MacOS => new(
        CreatePlatformInfo(CommandModifierKey.Command, treatsBackspaceAsDeleteKey: true),
        CreateStringLocalizer());

    private static ShortcutHintService Windows => new(
        CreatePlatformInfo(CommandModifierKey.Control, treatsCtrlYAsRedo: true),
        CreateStringLocalizer());

    private static ShortcutHintService Linux => new(
        CreatePlatformInfo(CommandModifierKey.Control),
        CreateStringLocalizer());

    [Test]
    public void EveryEditVerbDeclaresBothPlatformForms()
    {
        // The resource name is composed from the verb, so a verb added without its two entries would show a
        // raw resource name in the menu.
        var strings = TestLocalizerService.LoadStrings();
        strings.Should().NotBeEmpty("the application's en-US Resources.resw should be readable");

        foreach (var intent in Enum.GetValues<EditIntent>())
        {
            strings.Should().ContainKey($"Shortcut_{intent}Command");
            strings.Should().ContainKey($"Shortcut_{intent}Control");
        }

        strings.Should().ContainKey("Shortcut_RedoCtrlY");
    }

    [Test]
    public void GetText_EveryEditVerb_ResolvesAResource()
    {
        foreach (var intent in Enum.GetValues<EditIntent>())
        {
            foreach (var shortcutHintService in new[] { MacOS, Windows, Linux })
            {
                var hint = shortcutHintService.GetText(intent);

                hint.Should().NotBeNullOrWhiteSpace();
                hint.Should().NotStartWith("Shortcut_", "a missing resource resolves to its own name");
            }
        }
    }

    [Test]
    public void GetText_OnMacOS_MatchesTheChordsTheMenuBarHandles()
    {
        // The hint is display only, so it is useful only if it matches the chord the menu bar carries.
        var shortcutHintService = MacOS;

        foreach (var shortcut in MacOSEditShortcuts.All)
        {
            var expected = (shortcut.Shift ? "⇧⌘" : "⌘") + char.ToUpperInvariant(shortcut.Character);

            shortcutHintService.GetText(shortcut.Intent).Should().Be(expected);
        }
    }

    [Test]
    public void GetText_Redo_NamesTheChordThePlatformUses()
    {
        MacOS.GetText(EditIntent.Redo).Should().Be("⇧⌘Z");
        Windows.GetText(EditIntent.Redo).Should().Be("Ctrl+Y");
        Linux.GetText(EditIntent.Redo).Should().Be("Ctrl+Shift+Z");
    }

    [Test]
    public void GetText_Delete_NamesTheKeyThePlatformLabels()
    {
        MacOS.GetText(EditIntent.Delete).Should().Be("⌫");
        Windows.GetText(EditIntent.Delete).Should().Be("Del");
    }

    [Test]
    public void GetText_ANamedShortcut_PicksThePlatformForm()
    {
        MacOS.GetText("DocumentTab_CloseShortcut").Should().Be("⌘W");
        Windows.GetText("DocumentTab_CloseShortcut").Should().Be("Ctrl+W");
    }
}
