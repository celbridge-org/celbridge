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
/// Checks the Control-modifier shortcut hints against what the application actually does with those chords.
/// The macOS hints are checked against MacOSEditShortcuts, which installs the chords the menu bar carries.
/// The Control forms have no such table, so they are parsed back into a chord and fed to the handler that
/// owns them.
/// </summary>
[TestFixture]
public class ShortcutHintDriftTests
{
    private IMessengerService _messengerService = null!;
    private IPlatformInfo _platformInfo = null!;
    private KeyboardShortcutService _shortcutService = null!;
    private IReadOnlyDictionary<string, string> _strings = null!;
    private object _recipient = null!;

    [SetUp]
    public void Setup()
    {
        _messengerService = new MessengerService();
        _platformInfo = Substitute.For<IPlatformInfo>();
        _shortcutService = new KeyboardShortcutService(_messengerService, _platformInfo);
        _strings = TestLocalizerService.LoadStrings();
        _recipient = new object();
    }

    [TearDown]
    public void TearDown()
    {
        _messengerService.UnregisterAll(_recipient);
    }

    // Reads a hint such as "Ctrl+Shift+Z" back into the chord it names. Returns null for a hint that names
    // no key the shortcut table can match.
    private static (VirtualKey Key, bool Control, bool Shift, bool Alt)? ParseChord(string hint)
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

        return (
            key,
            modifiers.Contains("Ctrl"),
            modifiers.Contains("Shift"),
            modifiers.Contains("Alt"));
    }

    private string Hint(string resourceName)
    {
        _strings.Should().ContainKey(resourceName);

        return _strings[resourceName];
    }

    [Test]
    public void EveryControlHintNamesAChord()
    {
        // A hint is worth nothing if it cannot be read as a chord, so a typo or a changed separator fails
        // here rather than reaching a menu.
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
    public void UndoHint_NamesTheChordThatRequestsUndo()
    {
        var chord = ParseChord(Hint("Shortcut_UndoControl"))!.Value;

        var undoRequested = false;
        _messengerService.Register<UndoRequestedMessage>(_recipient, (r, m) => undoRequested = true);

        _shortcutService.HandleShortcut(chord.Key, chord.Control, chord.Shift, chord.Alt).Should().BeTrue();
        undoRequested.Should().BeTrue();
    }

    [Test]
    public void RedoHint_NamesTheChordThatRequestsRedo()
    {
        var crossPlatform = ParseChord(Hint("Shortcut_RedoControl"))!.Value;

        var redoRequested = false;
        _messengerService.Register<RedoRequestedMessage>(_recipient, (r, m) => redoRequested = true);

        _shortcutService.HandleShortcut(crossPlatform.Key, crossPlatform.Control, crossPlatform.Shift, crossPlatform.Alt)
            .Should().BeTrue();
        redoRequested.Should().BeTrue();
    }

    [Test]
    public void RedoCtrlYHint_NamesTheChordWindowsRequestsRedoWith()
    {
        _platformInfo.TreatsCtrlYAsRedo.Returns(true);

        var chord = ParseChord(Hint("Shortcut_RedoCtrlY"))!.Value;

        var redoRequested = false;
        _messengerService.Register<RedoRequestedMessage>(_recipient, (r, m) => redoRequested = true);

        _shortcutService.HandleShortcut(chord.Key, chord.Control, chord.Shift, chord.Alt).Should().BeTrue();
        redoRequested.Should().BeTrue();
    }

    [Test]
    public void CloseHints_NameTheChordsThatCloseDocuments()
    {
        var close = ParseChord(Hint("DocumentTab_CloseShortcutControl"))!.Value;
        var closeAll = ParseChord(Hint("DocumentTab_CloseAllShortcutControl"))!.Value;

        var closeRequested = false;
        var closeAllRequested = false;
        _messengerService.Register<CloseActiveDocumentRequestedMessage>(_recipient, (r, m) => closeRequested = true);
        _messengerService.Register<CloseAllDocumentsRequestedMessage>(_recipient, (r, m) => closeAllRequested = true);

        _shortcutService.HandleShortcut(close.Key, close.Control, close.Shift, close.Alt).Should().BeTrue();
        _shortcutService.HandleShortcut(closeAll.Key, closeAll.Control, closeAll.Shift, closeAll.Alt).Should().BeTrue();

        closeRequested.Should().BeTrue();
        closeAllRequested.Should().BeTrue();
    }
}
