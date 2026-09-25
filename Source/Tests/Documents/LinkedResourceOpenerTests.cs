using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Documents.Helpers;
using Celbridge.Explorer;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// A link in a document's preview, whether markdown or HTML, opens what it leads to in Celbridge.
/// These tests pin how that is decided: a file with an editor opens in it, anything else is selected in the
/// Explorer rather than raising the unsupported-format dialog, and a resource the project does not have is
/// left for the caller to report.
/// </summary>
[TestFixture]
public class LinkedResourceOpenerTests
{
    private ICommandService _commandService = null!;
    private IWorkspaceService _workspaceService = null!;
    private IOpenDocumentCommand _openCommand = null!;
    private ISelectResourceCommand _selectCommand = null!;

    [SetUp]
    public void Setup()
    {
        _workspaceService = Substitute.For<IWorkspaceService>();

        _openCommand = Substitute.For<IOpenDocumentCommand>();
        _selectCommand = Substitute.For<ISelectResourceCommand>();

        _commandService = Substitute.For<ICommandService>();
        _commandService
            .When(service => service.Execute(Arg.Any<Action<IOpenDocumentCommand>>(), Arg.Any<string>(), Arg.Any<int>()))
            .Do(call => call.Arg<Action<IOpenDocumentCommand>>().Invoke(_openCommand));
        _commandService
            .When(service => service.Execute(Arg.Any<Action<ISelectResourceCommand>>(), Arg.Any<string>(), Arg.Any<int>()))
            .Do(call => call.Arg<Action<ISelectResourceCommand>>().Invoke(_selectCommand));
    }

    [Test]
    public void AFileWithAnEditor_OpensInIt()
    {
        var resource = new ResourceKey("docs/notes.md");
        StubResourceOnDisk(resource, resource, isDocumentSupported: true);

        var isOpened = LinkedResourceOpener.Open(_commandService, _workspaceService, resource);

        isOpened.Should().BeTrue();
        _openCommand.FileResource.Should().Be(resource);
        _commandService.DidNotReceive().Execute(Arg.Any<Action<ISelectResourceCommand>>(), Arg.Any<string>(), Arg.Any<int>());
    }

    [TestCase("docs/archive.zip")]
    [TestCase("docs")]
    public void AnythingElse_IsSelectedInTheExplorer(string resourcePath)
    {
        var resource = new ResourceKey(resourcePath);
        StubResourceOnDisk(resource, resource, isDocumentSupported: false);

        var isOpened = LinkedResourceOpener.Open(_commandService, _workspaceService, resource);

        isOpened.Should().BeTrue();
        _selectCommand.Resource.Should().Be(resource);
        _commandService.DidNotReceive().Execute(Arg.Any<Action<IOpenDocumentCommand>>(), Arg.Any<string>(), Arg.Any<int>());
    }

    [Test]
    public void ALinkThatDiffersInCase_OpensTheResourceUnderItsNameOnDisk()
    {
        var linkedResource = new ResourceKey("docs/Notes.md");
        var resourceOnDisk = new ResourceKey("docs/notes.md");
        StubResourceOnDisk(linkedResource, resourceOnDisk, isDocumentSupported: true);

        LinkedResourceOpener.Open(_commandService, _workspaceService, linkedResource);

        _openCommand.FileResource.Should().Be(resourceOnDisk);
    }

    [Test]
    public void AResourceTheProjectDoesNotHave_IsNotOpened()
    {
        var resource = new ResourceKey("docs/missing.md");
        _workspaceService.ResourceService.Registry.NormalizeResourceKey(resource)
            .Returns(Result<ResourceKey>.Fail("Not in the project"));

        var isOpened = LinkedResourceOpener.Open(_commandService, _workspaceService, resource);

        isOpened.Should().BeFalse();
        _commandService.DidNotReceive().Execute(Arg.Any<Action<IOpenDocumentCommand>>(), Arg.Any<string>(), Arg.Any<int>());
        _commandService.DidNotReceive().Execute(Arg.Any<Action<ISelectResourceCommand>>(), Arg.Any<string>(), Arg.Any<int>());
    }

    private void StubResourceOnDisk(ResourceKey linkedResource, ResourceKey resourceOnDisk, bool isDocumentSupported)
    {
        _workspaceService.ResourceService.Registry.NormalizeResourceKey(linkedResource)
            .Returns(Result<ResourceKey>.Ok(resourceOnDisk));
        _workspaceService.DocumentsService.IsDocumentSupported(resourceOnDisk).Returns(isDocumentSupported);
    }
}
