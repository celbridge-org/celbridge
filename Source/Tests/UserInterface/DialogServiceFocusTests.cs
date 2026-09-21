using Celbridge.Dialog;
using Celbridge.Messaging;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.Services.Dialogs;
using Celbridge.WebHost;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Covers where DialogService leaves the keyboard once a dialog closes: with the control focus came back to
/// when that control sits in the focused panel, and back on the focused panel otherwise.
/// </summary>
[TestFixture]
public class DialogServiceFocusTests
{
    private IFocusService _focusService = null!;
    private IManagedFocus _managedFocus = null!;
    private IWebViewFocusRegistry _webViewFocusRegistry = null!;
    private DialogService _dialogService = null!;

    [SetUp]
    public void SetUp()
    {
        var alertDialog = Substitute.For<IAlertDialog>();
        alertDialog.ShowDialogAsync().Returns(Task.CompletedTask);

        var dialogFactory = Substitute.For<IDialogFactory>();
        dialogFactory.CreateAlertDialog(Arg.Any<string>(), Arg.Any<string>()).Returns(alertDialog);

        _focusService = Substitute.For<IFocusService>();
        _focusService.FocusedPanel.Returns(FocusPanelId.Documents);

        _managedFocus = Substitute.For<IManagedFocus>();
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
    public async Task FocusThatCameBackToTheFocusedPanel_StaysWhereTheDialogLeftIt()
    {
        // The packaged Windows head hands focus back to the control that opened the dialog, such as a browse
        // button inside a document. Refocusing the panel would move it to the document's default target.
        _managedFocus.Panel.Returns(FocusPanelId.Documents);

        await _dialogService.ShowAlertDialogAsync("Title", "Message");

        _focusService.DidNotReceive().RefocusPanel(Arg.Any<FocusPanelId>());
    }

    [Test]
    public async Task FocusThatDidNotComeBack_IsReturnedToTheFocusedPanel()
    {
        // The Skia heads can leave focus outside every panel once a dialog closes.
        _managedFocus.Panel.Returns(FocusPanelId.None);

        await _dialogService.ShowAlertDialogAsync("Title", "Message");

        _focusService.Received(1).RefocusPanel(FocusPanelId.Documents);
    }

    [Test]
    public async Task FocusThatCameBackToAnotherPanel_IsReturnedToTheFocusedPanel()
    {
        _managedFocus.Panel.Returns(FocusPanelId.Explorer);

        await _dialogService.ShowAlertDialogAsync("Title", "Message");

        _focusService.Received(1).RefocusPanel(FocusPanelId.Documents);
    }

    [Test]
    public async Task APanelWhoseWebSurfaceHeldTheKeyboard_IsRefocused()
    {
        // The page keeps its focus through the dialog, and only gets its caret back when its document takes
        // focus again.
        _webViewFocusRegistry.HasFocusedSurface.Returns(true);
        _managedFocus.Panel.Returns(FocusPanelId.Documents);

        await _dialogService.ShowAlertDialogAsync("Title", "Message");

        _focusService.Received(1).RefocusPanel(FocusPanelId.Documents);
    }

    [Test]
    public async Task ADialogOpenedWhileNoPanelHeldTheKeyboard_RefocusesNothing()
    {
        _focusService.FocusedPanel.Returns(FocusPanelId.None);

        await _dialogService.ShowAlertDialogAsync("Title", "Message");

        _focusService.DidNotReceive().RefocusPanel(Arg.Any<FocusPanelId>());
    }
}
