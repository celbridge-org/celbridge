using Celbridge.Commands;
using Celbridge.Resources;
using Celbridge.Server;
using Celbridge.Tools;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests that the DataTools MCP tool methods pass applySearchExclude to the commands that walk the project.
/// </summary>
[TestFixture]
public class DataToolTests
{
    private IApplicationServiceProvider _services = null!;
    private ICommandService _commandService = null!;

    [SetUp]
    public void SetUp()
    {
        _services = Substitute.For<IApplicationServiceProvider>();
        _commandService = Substitute.For<ICommandService>();

        _services.GetRequiredService<ICommandService>().Returns(_commandService);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FindTag_PassesApplySearchExclude(bool applySearchExclude)
    {
        var command = Substitute.For<IFindTagCommand>();
        IReadOnlyList<ResourceKey> noResources = Array.Empty<ResourceKey>();
        StubCommand(command, noResources);

        var tools = new DataTools(_services);
        await tools.FindTag("draft", applySearchExclude);

        command.Received().ApplySearchExclude = applySearchExclude;
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ListTags_PassesApplySearchExclude(bool applySearchExclude)
    {
        var command = Substitute.For<IListTagsCommand>();
        IReadOnlyList<string> noTags = Array.Empty<string>();
        StubCommand(command, noTags);

        var tools = new DataTools(_services);
        await tools.ListTags(applySearchExclude);

        command.Received().ApplySearchExclude = applySearchExclude;
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Inspect_PassesApplySearchExclude(bool applySearchExclude)
    {
        var command = Substitute.For<IInspectCommand>();
        var emptyResult = new InspectResult(Array.Empty<InspectRecord>(), new InspectSummary(0, 0, 0, 0, 0));
        StubCommand(command, emptyResult);

        var tools = new DataTools(_services);
        await tools.Inspect(applySearchExclude: applySearchExclude);

        command.Received().ApplySearchExclude = applySearchExclude;
    }

    // Runs the tool's configure action against the command substitute, and returns the result.
    private void StubCommand<TCommand, TResult>(TCommand command, TResult result)
        where TCommand : class, IExecutableCommand<TResult>
        where TResult : notnull
    {
        _commandService
            .ExecuteAsync<TCommand, TResult>(
                Arg.Any<Action<TCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<TCommand>?>();
                configure?.Invoke(command);
                return Task.FromResult(Result<TResult>.Ok(result));
            });
    }
}
