using Celbridge.Dialog;
using Celbridge.Messaging;
using Celbridge.UserInterface.Services.Dialogs;
using Celbridge.Validators;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

[TestFixture]
public class DialogServiceAnswerTests
{
    private CapturingMessengerService _messengerService = null!;
    private IDialogFactory _dialogFactory = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private ILogger<DialogService> _logger = null!;
    private DialogService _dialogService = null!;

    [SetUp]
    public void SetUp()
    {
        _messengerService = new CapturingMessengerService();
        _dialogFactory = Substitute.For<IDialogFactory>();
        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _logger = Substitute.For<ILogger<DialogService>>();

        _dialogService = new DialogService(_logger, _dialogFactory, _workspaceWrapper, _messengerService);

        // The DialogService awaits dialog.ShowDialogAsync; stub something sensible.
        var fakeConfirm = Substitute.For<IConfirmationDialog>();
        fakeConfirm.ShowDialogAsync().Returns(Task.FromResult(false));
        _dialogFactory.CreateConfirmationDialog(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(fakeConfirm);

        var fakeInputText = Substitute.For<IInputTextDialog>();
        fakeInputText.ShowDialogAsync().Returns(Task.FromResult(Result<string>.Fail("cancelled")));
        _dialogFactory.CreateInputTextDialog(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Range>(),
            Arg.Any<IValidator>(),
            Arg.Any<string?>()).Returns(fakeInputText);
    }

    [Test]
    public void ScheduleAnswer_AloneDoesNotBroadcast()
    {
        _dialogService.ScheduleAnswer(DialogKind.Confirmation, payload: "", delayMs: 0);

        // No dialog has been shown; the broadcast must not have fired.
        _messengerService.Sent<DialogAnswerMessage>().Should().BeEmpty();
    }

    [Test]
    public async Task ShowingConfirmationDialog_FiresScheduledAnswer()
    {
        _dialogService.ScheduleAnswer(DialogKind.Confirmation, payload: "", delayMs: 0);

        await _dialogService.ShowConfirmationDialogAsync("Title", "Message");
        await WaitForBroadcast<DialogAnswerMessage>();

        var sent = _messengerService.Sent<DialogAnswerMessage>();
        sent.Should().HaveCount(1);
        sent[0].Payload.Should().BeEmpty();
    }

    [Test]
    public async Task ShowingInputTextDialog_FiresScheduledAnswerWithPayload()
    {
        _dialogService.ScheduleAnswer(DialogKind.InputText, payload: "Renamed.txt", delayMs: 0);

        await _dialogService.ShowInputTextDialogAsync(
            "Title", "Message", "default", 0..0, Substitute.For<IValidator>());
        await WaitForBroadcast<DialogAnswerMessage>();

        var sent = _messengerService.Sent<DialogAnswerMessage>();
        sent.Should().HaveCount(1);
        sent[0].Payload.Should().Be("Renamed.txt");
    }

    [Test]
    public async Task ShowingConfirmationDialog_WithoutSchedule_DoesNotBroadcast()
    {
        await _dialogService.ShowConfirmationDialogAsync("Title", "Message");
        await Task.Delay(50);

        _messengerService.Sent<DialogAnswerMessage>().Should().BeEmpty();
    }

    [Test]
    public async Task ReSchedule_OverwritesPendingAnswer()
    {
        _dialogService.ScheduleAnswer(DialogKind.InputText, payload: "first", delayMs: 0);
        _dialogService.ScheduleAnswer(DialogKind.InputText, payload: "second", delayMs: 0);

        await _dialogService.ShowInputTextDialogAsync(
            "Title", "Message", "default", 0..0, Substitute.For<IValidator>());
        await WaitForBroadcast<DialogAnswerMessage>();

        var sent = _messengerService.Sent<DialogAnswerMessage>();
        sent.Should().HaveCount(1);
        sent[0].Payload.Should().Be("second");
    }

    [Test]
    public async Task WorkspaceUnloaded_ClearsSchedule()
    {
        _dialogService.ScheduleAnswer(DialogKind.Confirmation, payload: "", delayMs: 0);
        _messengerService.Send(new WorkspaceUnloadedMessage());

        await _dialogService.ShowConfirmationDialogAsync("Title", "Message");
        await Task.Delay(50);

        _messengerService.Sent<DialogAnswerMessage>().Should().BeEmpty();
    }

    [Test]
    public async Task DelayedSchedule_BroadcastsAfterAtLeastDelay()
    {
        const int delayMs = 80;
        _dialogService.ScheduleAnswer(DialogKind.Confirmation, payload: "", delayMs: delayMs);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await _dialogService.ShowConfirmationDialogAsync("Title", "Message");
        await WaitForBroadcast<DialogAnswerMessage>();
        stopwatch.Stop();

        stopwatch.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(delayMs - 20);
        _messengerService.Sent<DialogAnswerMessage>().Should().HaveCount(1);
    }

    [Test]
    public async Task ScheduleConsumedByFirstShowOnly()
    {
        _dialogService.ScheduleAnswer(DialogKind.Confirmation, payload: "", delayMs: 0);

        await _dialogService.ShowConfirmationDialogAsync("Title", "Message");
        await WaitForBroadcast<DialogAnswerMessage>();
        await _dialogService.ShowConfirmationDialogAsync("Title", "Message");
        await Task.Delay(50);

        _messengerService.Sent<DialogAnswerMessage>().Should().HaveCount(1);
    }

    [Test]
    public async Task KindMismatch_LeavesScheduleInPlace_AndIntendedDialogStillFires()
    {
        // Schedule for an input-text dialog, then show a confirmation dialog
        // first: the confirmation should not consume the schedule.
        _dialogService.ScheduleAnswer(DialogKind.InputText, payload: "Renamed.txt", delayMs: 0);

        await _dialogService.ShowConfirmationDialogAsync("Title", "Message");
        await Task.Delay(50);

        _messengerService.Sent<DialogAnswerMessage>().Should().BeEmpty();

        // The schedule is still pending; the input-text dialog now consumes it.
        await _dialogService.ShowInputTextDialogAsync(
            "Title", "Message", "default", 0..0, Substitute.For<IValidator>());
        await WaitForBroadcast<DialogAnswerMessage>();

        var sent = _messengerService.Sent<DialogAnswerMessage>();
        sent.Should().HaveCount(1);
        sent[0].Payload.Should().Be("Renamed.txt");
    }

    // Spin-wait until the messenger captures at least one message of the
    // requested type or a short timeout elapses, so tests don't race the
    // background broadcast task spawned by OnDialogShown.
    private async Task WaitForBroadcast<TMessage>(int timeoutMs = 500) where TMessage : class
    {
        var deadline = Environment.TickCount + timeoutMs;
        while (Environment.TickCount < deadline)
        {
            if (_messengerService.Sent<TMessage>().Count > 0)
            {
                return;
            }
            await Task.Delay(10);
        }
    }

    // Captures Send<T> calls so tests can assert on broadcast payloads
    // without sharing the static WeakReferenceMessenger.Default state with
    // other fixtures.
    private sealed class CapturingMessengerService : IMessengerService
    {
        private readonly Dictionary<Type, List<Action<object>>> _handlers = new();
        private readonly Dictionary<Type, List<object>> _recipients = new();
        private readonly Dictionary<Type, List<object>> _sent = new();
        private readonly object _gate = new();

        public IReadOnlyList<TMessage> Sent<TMessage>() where TMessage : class
        {
            lock (_gate)
            {
                if (_sent.TryGetValue(typeof(TMessage), out var list))
                {
                    return list.Cast<TMessage>().ToList();
                }
                return Array.Empty<TMessage>();
            }
        }

        public void Register<TMessage>(object recipient, MessageHandler<object, TMessage> handler)
            where TMessage : class
        {
            lock (_gate)
            {
                var type = typeof(TMessage);
                if (!_handlers.TryGetValue(type, out var list))
                {
                    list = new List<Action<object>>();
                    _handlers[type] = list;
                    _recipients[type] = new List<object>();
                }
                list.Add(msg => handler(recipient, (TMessage)msg));
                _recipients[type].Add(recipient);
            }
        }

        public void Unregister<TMessage>(object recipient) where TMessage : class
        {
            lock (_gate)
            {
                var type = typeof(TMessage);
                if (_recipients.TryGetValue(type, out var recipients))
                {
                    var index = recipients.IndexOf(recipient);
                    if (index >= 0)
                    {
                        recipients.RemoveAt(index);
                        _handlers[type].RemoveAt(index);
                    }
                }
            }
        }

        public void UnregisterAll(object recipient)
        {
            lock (_gate)
            {
                foreach (var type in _recipients.Keys.ToList())
                {
                    var recipients = _recipients[type];
                    for (int i = recipients.Count - 1; i >= 0; i--)
                    {
                        if (recipients[i] == recipient)
                        {
                            recipients.RemoveAt(i);
                            _handlers[type].RemoveAt(i);
                        }
                    }
                }
            }
        }

        public TMessage Send<TMessage>() where TMessage : class, new()
            => Send(new TMessage());

        public TMessage Send<TMessage>(TMessage message) where TMessage : class
        {
            List<Action<object>>? snapshot;
            lock (_gate)
            {
                if (!_sent.TryGetValue(typeof(TMessage), out var sentList))
                {
                    sentList = new List<object>();
                    _sent[typeof(TMessage)] = sentList;
                }
                sentList.Add(message);

                if (_handlers.TryGetValue(typeof(TMessage), out var list))
                {
                    snapshot = list.ToList();
                }
                else
                {
                    snapshot = null;
                }
            }

            if (snapshot is not null)
            {
                foreach (var handler in snapshot)
                {
                    handler(message);
                }
            }

            return message;
        }
    }
}
