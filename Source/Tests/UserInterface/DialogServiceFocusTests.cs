using Celbridge.Dialog;
using Celbridge.Messaging;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.Services.Dialogs;
using Celbridge.WebHost;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Covers where DialogService leaves the keyboard once a dialog closes: with the control that held it when
/// focus came back there by itself, back on that control when it did not, and on the panel that held it
/// when the control can no longer take it.
/// </summary>
[TestFixture]
public class DialogServiceFocusTests
{
    private IAlertDialog _alertDialog = null!;
    private IFocusService _focusService = null!;
    private IManagedFocus _managedFocus = null!;
    private INotedFocus _notedFocus = null!;
    private IWebViewFocusRegistry _webViewFocusRegistry = null!;
    private DialogService _dialogService = null!;

    [SetUp]
    public void SetUp()
    {
        _alertDialog = Substitute.For<IAlertDialog>();
        _alertDialog.ShowDialogAsync().Returns(Task.CompletedTask);

        var dialogFactory = Substitute.For<IDialogFactory>();
        dialogFactory.CreateAlertDialog(Arg.Any<string>(), Arg.Any<string>()).Returns(_alertDialog);

        _focusService = Substitute.For<IFocusService>();
        _focusService.FocusedPanel.Returns(FocusPanelId.Documents);

        _notedFocus = Substitute.For<INotedFocus>();
        _managedFocus = Substitute.For<IManagedFocus>();
        _managedFocus.NoteFocus().Returns(_notedFocus);

        _webViewFocusRegistry = Substitute.For<IWebViewFocusRegistry>();

        _dialogService = new DialogService(
            Substitute.For<ILogger<DialogService>>(),
            dialogFactory,
            _focusService,
            _managedFocus,
            _webViewFocusRegistry,
            Substitute.For<IWorkspaceWrapper>(),
            Substitute.For<IMessengerService>());
    }

    [Test]
    public async Task FocusThatCameBackToTheControlThatHeldIt_StaysThere()
    {
        // The packaged Windows head hands focus back to the control that opened the dialog, such as a browse
        // button inside a document. Refocusing the panel would move it to the document's default target.
        _notedFocus.IsFocusBack.Returns(true);

        await _dialogService.ShowAlertDialogAsync("Title", "Message");

        _notedFocus.DidNotReceive().TryReturnFocus();
        _focusService.DidNotReceive().RefocusPanel(Arg.Any<FocusPanelId>());
    }

    [Test]
    public async Task FocusThatDidNotComeBack_IsGivenBackToTheControlThatHeldIt()
    {
        // The Skia heads can leave focus on the first focusable element of any panel once a dialog closes,
        // including the panel the control belongs to.
        _notedFocus.IsFocusBack.Returns(false);
        _notedFocus.TryReturnFocus().Returns(true);

        await _dialogService.ShowAlertDialogAsync("Title", "Message");

        _notedFocus.Received(1).TryReturnFocus();
        _focusService.DidNotReceive().RefocusPanel(Arg.Any<FocusPanelId>());
    }

    [Test]
    public async Task AControlThatCannotTakeTheKeyboardBack_LeavesItToItsPanel()
    {
        // A control in a menu that has since closed, for example.
        _notedFocus.IsFocusBack.Returns(false);
        _notedFocus.TryReturnFocus().Returns(false);

        await _dialogService.ShowAlertDialogAsync("Title", "Message");

        _focusService.Received(1).RefocusPanel(FocusPanelId.Documents);
    }

    [Test]
    public async Task ThePanelThatHeldTheKeyboardBeforeTheDialog_IsTheOneRefocused()
    {
        // The focus model follows wherever the closing dialog leaves managed focus, so by then it can name
        // another panel.
        _alertDialog.ShowDialogAsync().Returns(_ =>
        {
            _focusService.FocusedPanel.Returns(FocusPanelId.Explorer);
            return Task.CompletedTask;
        });

        _notedFocus.IsFocusBack.Returns(false);
        _notedFocus.TryReturnFocus().Returns(false);

        await _dialogService.ShowAlertDialogAsync("Title", "Message");

        _focusService.Received(1).RefocusPanel(FocusPanelId.Documents);
    }

    [Test]
    public async Task APanelWhoseWebSurfaceHeldTheKeyboard_IsRefocused()
    {
        // The page keeps its focus through the dialog, and only gets its caret back when its document takes
        // focus again.
        _webViewFocusRegistry.HasFocusedSurface.Returns(true);
        _notedFocus.IsFocusBack.Returns(true);

        await _dialogService.ShowAlertDialogAsync("Title", "Message");

        _focusService.Received(1).RefocusPanel(FocusPanelId.Documents);
    }

    [Test]
    public async Task ADialogOpenedWhileNoPanelHeldTheKeyboard_RefocusesNothing()
    {
        _focusService.FocusedPanel.Returns(FocusPanelId.None);

        await _dialogService.ShowAlertDialogAsync("Title", "Message");

        _notedFocus.DidNotReceive().TryReturnFocus();
        _focusService.DidNotReceive().RefocusPanel(Arg.Any<FocusPanelId>());
    }
}
