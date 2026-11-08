using Celbridge.Community;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Utilities;
using Celbridge.WebHost;
using Microsoft.Extensions.Localization;

namespace Celbridge.WorkspaceUI.Services;

public class CommunityService : ICommunityService
{
    private const string DocumentName = "community";
    private const string CelbridgeIconResourceName = "Celbridge.WorkspaceUI.Assets.CelbridgeIcon.png";

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
        new ResourceKey($"{ProjectConstants.TempFolder}:{DocumentName}{ExplorerConstants.BrowserExtension}");

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

        // Every Community page uses the Celbridge icon. Each bookmark carries a copy of the icon. The bookmark list shows
        // the copy before the first visit. When the embedded icon fails to load, the bookmarks show the default icon.
        var iconResult = EmbeddedResourceReader.ReadBytes(typeof(CommunityService).Assembly, CelbridgeIconResourceName);
        if (iconResult.IsFailure)
        {
            _logger.LogWarning(iconResult, "Failed to read the Celbridge icon for the Community document");
        }
        var icon = iconResult.IsSuccess ? iconResult.Value : null;

        // The landing page opens when the document has no saved tabs, and the other sections are a click away.
        var bookmarks = new List<BrowserBookmark>
        {
            CreateBookmark(CommunityUrls.Celbridge, "Community_Section_Celbridge", icon, openOnStart: true),
            CreateBookmark(CommunityUrls.Learn, "Community_Section_Learn", icon),
            CreateBookmark(CommunityUrls.Forum, "Community_Section_Forum", icon)
        };

        var content = new BrowserFileContent
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

    // Names are resolved when the document is written, so a language change reaches the bookmarks on the next
    // workspace load.
    private BrowserBookmark CreateBookmark(string url, string nameKey, byte[]? icon, bool openOnStart = false)
    {
        return new BrowserBookmark(url, _stringLocalizer.GetString(nameKey), openOnStart, icon);
    }
}
