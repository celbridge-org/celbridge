using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Documents.Helpers;
using Celbridge.Explorer;
using Celbridge.Resources;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// A link in a document's preview, whether markdown or HTML, opens what it leads to in Celbridge.
/// These tests pin how that is decided. A file with an editor opens in that editor. Anything else is selected
/// in the Explorer, so the unsupported-format dialog never appears. OpenAsync fails for a missing resource and
/// for a link that differs from the stored name, and the caller reports both.
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
    public async Task AFileWithAnEditor_OpensInIt()
    {
        var resource = new ResourceKey("docs/notes.md");
        StubResourceOnDisk(resource, StorageItemKind.File, isDocumentSupported: true);

        var openResult = await LinkedResourceOpener.OpenAsync(_commandService, _workspaceService, resource);

        openResult.IsSuccess.Should().BeTrue();
        _openCommand.FileResource.Should().Be(resource);
        _commandService.DidNotReceive().Execute(Arg.Any<Action<ISelectResourceCommand>>(), Arg.Any<string>(), Arg.Any<int>());
    }

    [TestCase("docs/archive.zip", StorageItemKind.File)]
    [TestCase("docs", StorageItemKind.Folder)]
    public async Task AnythingElse_IsSelectedInTheExplorer(string resourcePath, StorageItemKind kind)
    {
        var resource = new ResourceKey(resourcePath);
        StubResourceOnDisk(resource, kind, isDocumentSupported: false);

        var openResult = await LinkedResourceOpener.OpenAsync(_commandService, _workspaceService, resource);

        openResult.IsSuccess.Should().BeTrue();
        _selectCommand.Resource.Should().Be(resource);
        _commandService.DidNotReceive().Execute(Arg.Any<Action<IOpenDocumentCommand>>(), Arg.Any<string>(), Arg.Any<int>());
    }

    [Test]
    public async Task ALinkThatDiffersInCase_IsRefusedWithTheStoredName()
    {
        var linkedResource = new ResourceKey("docs/Notes.md");
        var resourceOnDisk = new ResourceKey("docs/notes.md");
        var mismatch = new StoredNameMismatchError(linkedResource, resourceOnDisk);
        _workspaceService.ResourceService.FileSystem.GetInfoAsync(linkedResource)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Fail().WithException(mismatch)));

        var openResult = await LinkedResourceOpener.OpenAsync(_commandService, _workspaceService, linkedResource);

        openResult.IsFailure.Should().BeTrue();
        openResult.FirstException.Should().BeSameAs(mismatch);
        AssertNothingOpened();
    }

    [Test]
    public async Task AResourceTheProjectDoesNotHave_IsNotOpened()
    {
        var resource = new ResourceKey("docs/missing.md");
        StubResourceOnDisk(resource, StorageItemKind.NotFound, isDocumentSupported: true);

        var openResult = await LinkedResourceOpener.OpenAsync(_commandService, _workspaceService, resource);

        openResult.IsFailure.Should().BeTrue();
        AssertNothingOpened();
    }

    private void StubResourceOnDisk(ResourceKey resource, StorageItemKind kind, bool isDocumentSupported)
    {
        var info = new StorageItemInfo(kind, 0, DateTime.UtcNow, FileSystemAttributes.None);
        _workspaceService.ResourceService.FileSystem.GetInfoAsync(resource)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(info)));
        _workspaceService.DocumentsService.IsDocumentSupported(resource).Returns(isDocumentSupported);
    }

    private void AssertNothingOpened()
    {
        _commandService.DidNotReceive().Execute(Arg.Any<Action<IOpenDocumentCommand>>(), Arg.Any<string>(), Arg.Any<int>());
        _commandService.DidNotReceive().Execute(Arg.Any<Action<ISelectResourceCommand>>(), Arg.Any<string>(), Arg.Any<int>());
    }
}
