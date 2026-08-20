using Celbridge.Community;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.WebHost;

namespace Celbridge.WorkspaceUI.Services;

public class CommunityService : ICommunityService
{
    private readonly ILogger<CommunityService> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public CommunityService(
        ILogger<CommunityService> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public async Task SeedLinkDocumentsAsync()
    {
        foreach (var link in CommunityLinks.All)
        {
            var writeResult = await WriteLinkDocumentAsync(link);
            if (writeResult.IsFailure)
            {
                _logger.LogWarning(writeResult, $"Failed to seed the document for community link '{link.LinkId}'");
            }
        }
    }

    public CommunityLink? FindLink(string linkId)
    {
        foreach (var link in CommunityLinks.All)
        {
            if (link.LinkId == linkId)
            {
                return link;
            }
        }

        return null;
    }

    public ResourceKey GetLinkResource(CommunityLink link)
    {
        var documentName = $"{link.DocumentName}{ExplorerConstants.WebViewExtension}";

        return new ResourceKey($"{ProjectConstants.TempFolder}:{documentName}");
    }

    public async Task<Result> WriteLinkDocumentAsync(CommunityLink link)
    {
        // Seeding runs partway through the workspace load, so the page has not finished loading yet and
        // the presence of the workspace service is what says the write can reach the file system.
        if (!_workspaceWrapper.HasWorkspaceService)
        {
            return Result.Fail("Failed to write a community link document because no workspace is loaded");
        }

        var resource = GetLinkResource(link);

        // The URL bar stays on: the forum links out to the wider web, and the user needs the way back.
        var content = new WebViewFileContent(link.Url);

        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        var writeResult = await resourceFileSystem.WriteAllTextAsync(resource, content.ToToml());
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to write the document for community link '{link.LinkId}': '{resource}'")
                .WithErrors(writeResult);
        }

        return Result.Ok();
    }
}
