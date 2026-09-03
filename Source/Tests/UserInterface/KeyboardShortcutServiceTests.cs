using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Services;
using Windows.System;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the global keyboard shortcut table. A real MessengerService carries the requests the
/// service broadcasts, so each test asserts on the message a chord produces.
/// </summary>
[TestFixture]
public class KeyboardShortcutServiceTests
{
    private IMessengerService _messengerService = null!;
    private KeyboardShortcutService _shortcutService = null!;
    private object _recipient = null!;

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

    [Test]
    public void CommandW_RequestsCloseActiveDocument()
    {
        var closeRequested = false;
        _messengerService.Register<CloseActiveDocumentRequestedMessage>(_recipient, (r, m) => closeRequested = true);

        var handled = _shortcutService.HandleShortcut(VirtualKey.W, control: true, shift: false, alt: false);

        handled.Should().BeTrue();
        closeRequested.Should().BeTrue();
    }

    [Test]
    public void CommandShiftW_RequestsCloseAllDocuments()
    {
        var closeAllRequested = false;
        _messengerService.Register<CloseAllDocumentsRequestedMessage>(_recipient, (r, m) => closeAllRequested = true);

        var handled = _shortcutService.HandleShortcut(VirtualKey.W, control: true, shift: true, alt: false);

        handled.Should().BeTrue();
        closeAllRequested.Should().BeTrue();
    }

    [Test]
    public void UnmodifiedKey_RequestsNothing()
    {
        // Every character typed into a hosted editor reaches the table as a managed key event, so an
        // unmodified key must never match a shortcut.
        var closeRequested = false;
        _messengerService.Register<CloseActiveDocumentRequestedMessage>(_recipient, (r, m) => closeRequested = true);

        var closeHandled = _shortcutService.HandleShortcut(VirtualKey.W, control: false, shift: false, alt: false);

        closeHandled.Should().BeFalse();
        closeRequested.Should().BeFalse();
    }

    [Test]
    public void KeyName_ResolvesToTheSameShortcut()
    {
        // The WebView RPC entry point reports the key by name.
        var closeRequested = false;
        _messengerService.Register<CloseActiveDocumentRequestedMessage>(_recipient, (r, m) => closeRequested = true);

        var handled = _shortcutService.HandleShortcut("w", control: true, shift: false, alt: false);

        handled.Should().BeTrue();
        closeRequested.Should().BeTrue();
    }
}
