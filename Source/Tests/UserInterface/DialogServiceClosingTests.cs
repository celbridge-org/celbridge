using Celbridge.Dialog;
using Celbridge.Messaging;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.Services.Dialogs;
using Celbridge.WebHost;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Covers a dialog requested while another is open: it waits for one that has started to close, and is
/// refused otherwise.
/// </summary>
[TestFixture]
public class DialogServiceClosingTests
{
    private TaskCompletionSource<bool> _firstAnswer = null!;
    private IConfirmationDialog _secondDialog = null!;
    private IDialogFactory _dialogFactory = null!;
    private DialogService _dialogService = null!;

    [SetUp]
    public void SetUp()
    {
        _firstAnswer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstDialog = Substitute.For<IConfirmationDialog>();
        firstDialog.ShowDialogAsync().Returns(_firstAnswer.Task);

        _secondDialog = Substitute.For<IConfirmationDialog>();
        _secondDialog.ShowDialogAsync().Returns(Task.FromResult(true));

        _dialogFactory = Substitute.For<IDialogFactory>();
        _dialogFactory.CreateConfirmationDialog("First", Arg.Any<string>(), Arg.Any<ConfirmationDialogOptions?>())
            .Returns(firstDialog);
        _dialogFactory.CreateConfirmationDialog("Second", Arg.Any<string>(), Arg.Any<ConfirmationDialogOptions?>())
            .Returns(_secondDialog);

        var managedFocus = Substitute.For<IManagedFocus>();
        managedFocus.NoteFocus().Returns(Substitute.For<INotedFocus>());

        _dialogService = new DialogService(
            Substitute.For<ILogger<DialogService>>(),
            _dialogFactory,
            Substitute.For<IFocusService>(),
            managedFocus,
            Substitute.For<IWebViewFocusRegistry>(),
            Substitute.For<IWorkspaceWrapper>(),
            Substitute.For<IMessengerService>());
    }

    [Test]
    public async Task ADialogRequestedWhileTheLastOneCloses_OpensOnceItHasClosed()
    {
        // WinUI gives the keyboard back to the control that opened a dialog as the dialog starts to close, so a
        // key pressed on that control can ask for the dialog again before the first one has closed.
        var first = _dialogService.ShowConfirmationDialogAsync("First", "Message");
        _dialogService.OnDialogStartedClosing();

        var second = _dialogService.ShowConfirmationDialogAsync("Second", "Message");
        _ = _secondDialog.DidNotReceive().ShowDialogAsync();

        _firstAnswer.SetResult(false);
        await first;
        var secondResult = await second;

        secondResult.IsSuccess.Should().BeTrue();
        _ = _secondDialog.Received(1).ShowDialogAsync();
    }

    [Test]
    public async Task ADialogRequestedWhileAnotherIsOpen_IsRefused()
    {
        var first = _dialogService.ShowConfirmationDialogAsync("First", "Message");

        var secondResult = await _dialogService.ShowConfirmationDialogAsync("Second", "Message");

        secondResult.IsFailure.Should().BeTrue();
        _ = _secondDialog.DidNotReceive().ShowDialogAsync();

        _firstAnswer.SetResult(false);
        await first;
    }

    [Test]
    public async Task ADialogThatStopsClosing_RefusesTheWaitingRequest()
    {
        // A dialog can cancel its closing and stay open. The request waiting for it is then refused rather than
        // held until the dialog closes.
        _dialogService.ClosingWaitLimit = TimeSpan.FromMilliseconds(50);
        var first = _dialogService.ShowConfirmationDialogAsync("First", "Message");
        _dialogService.OnDialogStartedClosing();

        var secondResult = await _dialogService.ShowConfirmationDialogAsync("Second", "Message");

        secondResult.IsFailure.Should().BeTrue();
        _ = _secondDialog.DidNotReceive().ShowDialogAsync();

        _firstAnswer.SetResult(false);
        await first;
    }

    [Test]
    public async Task TheProgressDialog_StaysHiddenWhileARequestWaitsForAClosingDialog()
    {
        // Showing the progress dialog between the two would put two dialogs on screen as the second one opens.
        var first = _dialogService.ShowConfirmationDialogAsync("First", "Message");
        using var token = _dialogService.AcquireProgressDialog("Working");
        _dialogService.OnDialogStartedClosing();
        var second = _dialogService.ShowConfirmationDialogAsync("Second", "Message");

        _firstAnswer.SetResult(false);
        await first;
        await second;

        _ = _secondDialog.Received(1).ShowDialogAsync();
        _dialogFactory.Received(1).CreateProgressDialog();
    }
}
