using Celbridge.Explorer.Views;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Platform;
using Celbridge.Tests.Localization;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;
using Windows.System;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// The chord a shortcut hint names.
/// </summary>
internal sealed partial record ShortcutChord(VirtualKey Key, bool Control, bool Shift, bool Alt);

/// <summary>
/// Checks the Control-modifier shortcut hints against what the application actually does with those chords.
/// Each hint is parsed back into a chord and fed to the handler that owns it.
/// </summary>
[TestFixture]
public class ShortcutHintDriftTests
{
    private IMessengerService _messengerService = null!;
    private KeyboardShortcutService _shortcutService = null!;
    private object _recipient = null!;

    // LoadStrings parses the resw file on every call.
    private static readonly IReadOnlyDictionary<string, string> _strings = TestLocalizerService.LoadStrings();

    [SetUp]
    public void Setup()
    {
        _messengerService = new MessengerService();
        _shortcutService = new KeyboardShortcutService(_messengerService);
        _recipient = new object();
    }

    [TearDown]
    public void TearDown()
    {
        _messengerService.UnregisterAll(_recipient);
    }

    // Reads a hint such as "Ctrl+Shift+Z" back into the chord it names. Returns null for a hint that names
    // no key the shortcut table can match.
    private static ShortcutChord? ParseChord(string hint)
    {
        var parts = hint.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var keyName = parts[^1];
        var key = keyName switch
        {
            "Del" => VirtualKey.Delete,
            "F2" => VirtualKey.F2,
            _ when keyName.Length == 1 && keyName[0] is >= 'A' and <= 'Z' => Enum.Parse<VirtualKey>(keyName),
            _ => VirtualKey.None
        };

        if (key == VirtualKey.None)
        {
            return null;
        }

        var modifiers = parts[..^1];
        if (modifiers.Any(modifier => modifier is not ("Ctrl" or "Shift" or "Alt")))
        {
            return null;
        }

        return new ShortcutChord(
            key,
            Control: modifiers.Contains("Ctrl"),
            Shift: modifiers.Contains("Shift"),
            Alt: modifiers.Contains("Alt"));
    }

    // The resource's value, falling back to its name the way a localizer does for a missing entry.
    private static string Hint(string resourceName)
    {
        return _strings.TryGetValue(resourceName, out var value) ? value : resourceName;
    }

    [Test]
    public void EveryControlHintNamesAChord()
    {
        var resourceNames = Enum.GetValues<EditIntent>()
            .Select(intent => $"Shortcut_{intent}Control")
            .Append("Shortcut_RedoCtrlY")
            .Append("DocumentTab_CloseShortcutControl")
            .Append("DocumentTab_CloseAllShortcutControl");

        foreach (var resourceName in resourceNames)
        {
            ParseChord(Hint(resourceName))
                .Should().NotBeNull($"{resourceName} should name a chord the shortcut table can match");
        }
    }

    [Test]
    public void CloseHints_NameTheChordsThatCloseDocuments()
    {
        var close = ParseChord(Hint("DocumentTab_CloseShortcutControl"))!;
        var closeAll = ParseChord(Hint("DocumentTab_CloseAllShortcutControl"))!;

        var closeRequested = false;
        var closeAllRequested = false;
        _messengerService.Register<CloseActiveDocumentRequestedMessage>(_recipient, (r, m) => closeRequested = true);
        _messengerService.Register<CloseAllDocumentsRequestedMessage>(_recipient, (r, m) => closeAllRequested = true);

        _shortcutService.HandleShortcut(close.Key, close.Control, close.Shift, close.Alt).Should().BeTrue();
        _shortcutService.HandleShortcut(closeAll.Key, closeAll.Control, closeAll.Shift, closeAll.Alt).Should().BeTrue();

        closeRequested.Should().BeTrue();
        closeAllRequested.Should().BeTrue();
    }

    [Test]
    public void UndoHint_NamesTheChordExplorerEditShortcutsResolvesToUndo()
    {
        var chord = ParseChord(Hint("Shortcut_UndoControl"))!;

        ExplorerEditShortcuts.ResolveIntent(chord.Key, chord.Shift, treatsCtrlYAsRedo: false)
            .Should().Be(EditIntent.Undo);
    }

    [Test]
    public void RedoHint_NamesTheChordExplorerEditShortcutsResolvesToRedo()
    {
        var chord = ParseChord(Hint("Shortcut_RedoControl"))!;

        ExplorerEditShortcuts.ResolveIntent(chord.Key, chord.Shift, treatsCtrlYAsRedo: false)
            .Should().Be(EditIntent.Redo);
    }

    [Test]
    public void RedoCtrlYHint_NamesTheChordWindowsResolvesToRedo()
    {
        var chord = ParseChord(Hint("Shortcut_RedoCtrlY"))!;

        ExplorerEditShortcuts.ResolveIntent(chord.Key, chord.Shift, treatsCtrlYAsRedo: true)
            .Should().Be(EditIntent.Redo);

        // Named for Windows: the chord must not resolve to Redo where the platform does not treat it that way.
        ExplorerEditShortcuts.ResolveIntent(chord.Key, chord.Shift, treatsCtrlYAsRedo: false)
            .Should().NotBe(EditIntent.Redo);
    }
}
