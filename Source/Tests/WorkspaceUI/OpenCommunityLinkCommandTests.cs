using Celbridge.Commands;
using Celbridge.Community;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Commands;

namespace Celbridge.Tests.WorkspaceUI;

/// <summary>
/// Verifies when OpenCommunityLinkCommand regenerates a link's document. Regenerating an open document would
/// reload the web view out from under the reader, so it only happens while the document is closed.
/// </summary>
[TestFixture]
public class OpenCommunityLinkCommandTests
{
    private static readonly ResourceKey ForumResource = new("temp:forum.webview");

    private static readonly CommunityLink TestLink = new()
    {
        LinkId = "forum",
        DocumentName = "forum",
        Url = "https://example.com/forum",
        TooltipKey = "UtilityPanel_ForumTooltip"
    };

    private ICommandService _commandService = null!;
    private ICommunityService _communityService = null!;
    private IDocumentsService _documentsService = null!;
    private IOpenDocumentCommand _openDocumentCommand = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _openDocumentCommand = Substitute.For<IOpenDocumentCommand>();

        _commandService = Substitute.For<ICommandService>();
        _commandService
            .Execute<IOpenDocumentCommand>(
                Arg.Any<Action<IOpenDocumentCommand>>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<IOpenDocumentCommand>>();
                configure?.Invoke(_openDocumentCommand);

                return Result.Ok();
            });

        _communityService = Substitute.For<ICommunityService>();
        _communityService.FindLink(TestLink.LinkId).Returns(TestLink);
        _communityService.GetLinkResource(TestLink).Returns(ForumResource);
        _communityService.WriteLinkDocumentAsync(TestLink).Returns(Result.Ok());

        // No documents are open by default, so the command takes the regenerate path.
        _documentsService = Substitute.For<IDocumentsService>();
        _documentsService.GetOpenDocuments().Returns(new List<OpenDocumentInfo>());

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.DocumentsService.Returns(_documentsService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
    }

    private OpenCommunityLinkCommand CreateCommand()
    {
        return new OpenCommunityLinkCommand(_commandService, _communityService, _workspaceWrapper)
        {
            LinkId = TestLink.LinkId
        };
    }

    private void GivenTheDocumentIsOpen()
    {
        var openDocument = new OpenDocumentInfo(
            ForumResource,
            new DocumentAddress(WindowIndex: 0, Section: DocumentSection.MainLeft, TabOrder: 0),
            EditorId.Empty);

        _documentsService.GetOpenDocuments().Returns(new List<OpenDocumentInfo> { openDocument });
    }

    [Test]
    public async Task ExecuteAsync_DocumentIsClosed_RegeneratesThenOpensIt()
    {
        var command = CreateCommand();

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        await _communityService.Received(1).WriteLinkDocumentAsync(TestLink);
        _openDocumentCommand.FileResource.Should().Be(ForumResource);
    }

    [Test]
    public async Task ExecuteAsync_DocumentIsAlreadyOpen_OpensItWithoutRegenerating()
    {
        GivenTheDocumentIsOpen();

        var command = CreateCommand();

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        await _communityService.DidNotReceive().WriteLinkDocumentAsync(Arg.Any<CommunityLink>());
        _openDocumentCommand.FileResource.Should().Be(ForumResource);
    }

    [Test]
    public async Task ExecuteAsync_UnknownLinkId_Fails()
    {
        var command = CreateCommand();
        command.LinkId = "no-such-link";

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        _commandService.DidNotReceive().Execute<IOpenDocumentCommand>(
            Arg.Any<Action<IOpenDocumentCommand>>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    [Test]
    public async Task ExecuteAsync_WriteFails_DoesNotOpenTheDocument()
    {
        _communityService.WriteLinkDocumentAsync(TestLink).Returns(Result.Fail("Disk is full"));

        var command = CreateCommand();

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        _commandService.DidNotReceive().Execute<IOpenDocumentCommand>(
            Arg.Any<Action<IOpenDocumentCommand>>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }
}
