using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.UserInterface;
using Celbridge.Utilities;
using Celbridge.WebHost;
using Celbridge.Workshop;
using Microsoft.Extensions.Localization;

namespace Celbridge.WorkspaceUI.Services;

public class WorkshopService : IWorkshopService
{
    private const string DocumentName = "workshop";
    private const string CelbridgeIconResourceName = "Celbridge.WorkspaceUI.Assets.CelbridgeIcon.png";

    private readonly ILogger<WorkshopService> _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public WorkshopService(
        ILogger<WorkshopService> logger,
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
            _logger.LogWarning(writeResult, "Failed to seed the Workshop document");
        }
    }

    private async Task<Result> WriteDocumentAsync()
    {
        // Seeding runs partway through the workspace load, so the page has not finished loading yet and
        // the presence of the workspace service is what says the write can reach the file system.
        if (!_workspaceWrapper.HasWorkspaceService)
        {
            return Result.Fail("Failed to write the Workshop document because no workspace is loaded");
        }

        var resource = DocumentResource;

        // The bookmark has a copy of the Celbridge icon, so the bookmark list shows the icon before the first visit.
        // When the embedded icon fails to load, the bookmark shows the default icon.
        var iconResult = EmbeddedResourceReader.ReadBytes(typeof(WorkshopService).Assembly, CelbridgeIconResourceName);
        if (iconResult.IsFailure)
        {
            _logger.LogWarning(iconResult, "Failed to read the Celbridge icon for the Workshop document");
        }
        var icon = iconResult.IsSuccess ? iconResult.Value : null;

        // The name is resolved when the document is written, so a language change reaches the bookmark on the next
        // workspace load. The bookmark opens when the document has no saved tabs.
        string bookmarkName = _stringLocalizer.GetString("Workshop_BookmarkName");
        var bookmark = new BrowserBookmark(CelbridgeUrls.Workshop, bookmarkName, OpenOnStart: true, icon);

        var content = new BrowserFileContent
        {
            Bookmarks = [bookmark]
        };

        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        var writeResult = await resourceFileSystem.WriteAllTextAsync(resource, content.ToToml());
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to write the Workshop document: '{resource}'")
                .WithErrors(writeResult);
        }

        // Make the file read-only, so the user can't edit the bookmark. The next load writes the file again.
        var lockResult = await resourceFileSystem.SetAttributesAsync(resource, FileSystemAttributes.ReadOnly, set: true);
        if (lockResult.IsFailure)
        {
            return Result.Fail($"Failed to make the Workshop document read-only: '{resource}'")
                .WithErrors(lockResult);
        }

        return Result.Ok();
    }
}
