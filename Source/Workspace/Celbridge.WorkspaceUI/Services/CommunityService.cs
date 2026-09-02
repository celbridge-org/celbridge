using Celbridge.Community;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.WebHost;
using Microsoft.Extensions.Localization;

namespace Celbridge.WorkspaceUI.Services;

public class CommunityService : ICommunityService
{
    private const string DocumentName = "community";

    private readonly ILogger<CommunityService> _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public CommunityService(
        ILogger<CommunityService> logger,
        IStringLocalizer stringLocalizer,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _stringLocalizer = stringLocalizer;
        _workspaceWrapper = workspaceWrapper;
    }

    public ResourceKey DocumentResource =>
        new ResourceKey($"{ProjectConstants.TempFolder}:{DocumentName}{ExplorerConstants.WebViewExtension}");

    public async Task SeedDocumentAsync()
    {
        var writeResult = await WriteDocumentAsync();
        if (writeResult.IsFailure)
        {
            _logger.LogWarning(writeResult, "Failed to seed the Community document");
        }
    }

    private async Task<Result> WriteDocumentAsync()
    {
        // Seeding runs partway through the workspace load, so the page has not finished loading yet and
        // the presence of the workspace service is what says the write can reach the file system.
        if (!_workspaceWrapper.HasWorkspaceService)
        {
            return Result.Fail("Failed to write the Community document because no workspace is loaded");
        }

        var resource = DocumentResource;

        // The landing page is bookmarked as well as being the Home target, so the bookmarks bar alone is a
        // complete way around the site.
        var bookmarks = new List<WebViewBookmark>
        {
            CreateBookmark(CommunityUrls.Celbridge, "Community_Section_Celbridge", "bs-house"),
            CreateBookmark(CommunityUrls.Learn, "Community_Section_Learn", "bs-book"),
            CreateBookmark(CommunityUrls.Forum, "Community_Section_Forum", "bs-chat-dots")
        };

        // The URL bar stays on: the sections link out to the wider web, and the user needs the way back.
        var content = new WebViewFileContent(CommunityUrls.Celbridge)
        {
            Bookmarks = bookmarks
        };

        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        var writeResult = await resourceFileSystem.WriteAllTextAsync(resource, content.ToToml());
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to write the Community document: '{resource}'")
                .WithErrors(writeResult);
        }

        return Result.Ok();
    }

    // Names are resolved at seed time rather than when the bar is drawn, so a language change reaches the
    // bookmarks on the next workspace load.
    private WebViewBookmark CreateBookmark(string url, string nameKey, string iconName)
    {
        return new WebViewBookmark(url, _stringLocalizer.GetString(nameKey), iconName);
    }
}
