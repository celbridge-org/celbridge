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
    private MessageHandler<object, WorkspaceItemSaveFailuresChangedMessage>? _saveFailuresHandler;

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
                Arg.Any<MessageHandler<object, WorkspaceItemSaveFailuresChangedMessage>>()))
            .Do(call => _saveFailuresHandler = call.Arg<MessageHandler<object, WorkspaceItemSaveFailuresChangedMessage>>());

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
    public void SaveFailures_NameTheResource_WhenOneItemIsFailing()
    {
        _viewModel.OnLoaded();

        SendFailingResources(new ResourceKey("project:notes.txt"));

        _viewModel.HasSaveFailures.Should().BeTrue();
        _viewModel.SaveFailureMessage.Should().Be("SaveStatus_Failed_Single(notes.txt)");
    }

    [Test]
    public void SaveFailures_ShowTheCount_WhenSeveralItemsAreFailing()
    {
        _viewModel.OnLoaded();

        SendFailingResources(new ResourceKey("project:notes.txt"), new ResourceKey("project:data.json"));

        _viewModel.HasSaveFailures.Should().BeTrue();
        _viewModel.SaveFailureMessage.Should().Be("SaveStatus_Failed_Multiple(2)",
            "the names do not fit a tooltip");
    }

    [Test]
    public void SaveFailures_Clear_WhenTheFailingSetEmpties()
    {
        _viewModel.OnLoaded();

        SendFailingResources(new ResourceKey("project:notes.txt"));
        SendFailingResources();

        _viewModel.HasSaveFailures.Should().BeFalse();
        _viewModel.SaveFailureMessage.Should().BeEmpty();
    }

    [Test]
    public void WorkspaceUnloaded_ClearsTheSaveState()
    {
        _viewModel.OnLoaded();

        SendFailingResources(new ResourceKey("project:notes.txt"));
        _pendingSaveCountHandler!.Invoke(this, new PendingSaveCountMessage(1));

        _workspaceUnloadedHandler!.Invoke(this, new WorkspaceUnloadedMessage());

        _viewModel.IsSaving.Should().BeFalse("the save state belonged to the workspace that went away");
        _viewModel.HasSaveFailures.Should().BeFalse();
        _viewModel.SaveFailureMessage.Should().BeEmpty();
    }

    [Test]
    public void WorkspaceLoaded_DoesNotCarryFailuresOverFromThePreviousWorkspace()
    {
        _viewModel.OnLoaded();

        SendFailingResources(new ResourceKey("project:notes.txt"));
        _workspaceUnloadedHandler!.Invoke(this, new WorkspaceUnloadedMessage());

        _workspaceLoadedHandler!.Invoke(this, new WorkspaceLoadedMessage());

        _viewModel.HasSaveFailures.Should().BeFalse();
        _viewModel.SaveFailureMessage.Should().BeEmpty();
    }

    private void SendFailingResources(params ResourceKey[] failingResources)
    {
        _saveFailuresHandler!.Invoke(this, new WorkspaceItemSaveFailuresChangedMessage(failingResources));
    }
}
