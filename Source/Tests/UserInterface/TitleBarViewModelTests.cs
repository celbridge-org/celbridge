using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.UserInterface.ViewModels.Controls;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Tests for the save indicators in the application toolbar.
/// </summary>
[TestFixture]
public class TitleBarViewModelTests
{
    private IMessengerService _messengerService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IStringLocalizer _stringLocalizer = null!;

    private MessageHandler<object, WorkspaceLoadedMessage>? _workspaceLoadedHandler;
    private MessageHandler<object, WorkspaceUnloadedMessage>? _workspaceUnloadedHandler;
    private MessageHandler<object, PendingSaveCountMessage>? _pendingSaveCountHandler;
    private MessageHandler<object, WorkspaceItemSaveRetriesChangedMessage>? _saveRetriesHandler;

    private TitleBarViewModel _viewModel = null!;

    [SetUp]
    public void Setup()
    {
        _messengerService = Substitute.For<IMessengerService>();

        // Capture the handlers so a test can deliver a message without a live messenger.
        _messengerService
            .When(service => service.Register(
                Arg.Any<object>(),
                Arg.Any<MessageHandler<object, WorkspaceLoadedMessage>>()))
            .Do(call => _workspaceLoadedHandler = call.Arg<MessageHandler<object, WorkspaceLoadedMessage>>());

        _messengerService
            .When(service => service.Register(
                Arg.Any<object>(),
                Arg.Any<MessageHandler<object, WorkspaceUnloadedMessage>>()))
            .Do(call => _workspaceUnloadedHandler = call.Arg<MessageHandler<object, WorkspaceUnloadedMessage>>());

        _messengerService
            .When(service => service.Register(
                Arg.Any<object>(),
                Arg.Any<MessageHandler<object, PendingSaveCountMessage>>()))
            .Do(call => _pendingSaveCountHandler = call.Arg<MessageHandler<object, PendingSaveCountMessage>>());

        _messengerService
            .When(service => service.Register(
                Arg.Any<object>(),
                Arg.Any<MessageHandler<object, WorkspaceItemSaveRetriesChangedMessage>>()))
            .Do(call => _saveRetriesHandler = call.Arg<MessageHandler<object, WorkspaceItemSaveRetriesChangedMessage>>());

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();

        // The composed value carries the arguments, so a test can tell which values reached the template.
        _stringLocalizer = Substitute.For<IStringLocalizer>();
        _stringLocalizer[Arg.Any<string>(), Arg.Any<object[]>()].Returns(call =>
        {
            var name = call.Arg<string>();
            var arguments = call.Arg<object[]>();
            return new LocalizedString(name, $"{name}({string.Join(", ", arguments)})");
        });

        _viewModel = new TitleBarViewModel(_messengerService, _workspaceWrapper, _stringLocalizer);
    }

    [Test]
    public void SaveRetries_NameTheResource_WhenOneItemIsRetrying()
    {
        _viewModel.OnLoaded();

        SendRetryingResources(new ResourceKey("project:notes.txt"));

        _viewModel.HasSaveRetries.Should().BeTrue();
        _viewModel.SaveRetryMessage.Should().Be("SaveStatus_Failed_Single(notes.txt)");
    }

    [Test]
    public void SaveRetries_ShowTheCount_WhenSeveralItemsAreRetrying()
    {
        _viewModel.OnLoaded();

        SendRetryingResources(new ResourceKey("project:notes.txt"), new ResourceKey("project:data.json"));

        _viewModel.HasSaveRetries.Should().BeTrue();
        _viewModel.SaveRetryMessage.Should().Be("SaveStatus_Failed_Multiple(2)",
            "the names do not fit a tooltip");
    }

    [Test]
    public void SaveRetries_Clear_WhenTheRetryingSetEmpties()
    {
        _viewModel.OnLoaded();

        SendRetryingResources(new ResourceKey("project:notes.txt"));
        SendRetryingResources();

        _viewModel.HasSaveRetries.Should().BeFalse();
        _viewModel.SaveRetryMessage.Should().BeEmpty();
    }

    [Test]
    public void WorkspaceUnloaded_ClearsTheSaveState()
    {
        _viewModel.OnLoaded();

        SendRetryingResources(new ResourceKey("project:notes.txt"));
        _pendingSaveCountHandler!.Invoke(this, new PendingSaveCountMessage(1));

        _workspaceUnloadedHandler!.Invoke(this, new WorkspaceUnloadedMessage());

        _viewModel.IsSaving.Should().BeFalse("the save state belonged to the workspace that went away");
        _viewModel.HasSaveRetries.Should().BeFalse();
        _viewModel.SaveRetryMessage.Should().BeEmpty();
    }

    [Test]
    public void WorkspaceLoaded_DoesNotCarryFailuresOverFromThePreviousWorkspace()
    {
        _viewModel.OnLoaded();

        SendRetryingResources(new ResourceKey("project:notes.txt"));
        _workspaceUnloadedHandler!.Invoke(this, new WorkspaceUnloadedMessage());

        _workspaceLoadedHandler!.Invoke(this, new WorkspaceLoadedMessage());

        _viewModel.HasSaveRetries.Should().BeFalse();
        _viewModel.SaveRetryMessage.Should().BeEmpty();
    }

    private void SendRetryingResources(params ResourceKey[] retryingResources)
    {
        _saveRetriesHandler!.Invoke(this, new WorkspaceItemSaveRetriesChangedMessage(retryingResources));
    }
}
