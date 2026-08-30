using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.WebHost;
using Celbridge.Workshop;
using Microsoft.Extensions.Localization;

namespace Celbridge.WorkspaceUI.Services;

public class WorkshopService : IWorkshopService
{
    private const string DocumentName = "workshop";

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
        new ResourceKey($"{ProjectConstants.TempFolder}:{DocumentName}{ExplorerConstants.WebViewExtension}");

    public async Task SeedDocumentAsync()
    {
        var writeResult = await WriteDocumentAsync();
        if (writeResult.IsFailure)
        {
            _logger.LogWarning(writeResult, "Failed to seed the Workshop document");
        }
    }

    public async Task<Result> WriteDocumentAsync()
    {
        // Seeding runs partway through the workspace load, so the page has not finished loading yet and
        // the presence of the workspace service is what says the write can reach the file system.
        if (!_workspaceWrapper.HasWorkspaceService)
        {
            return Result.Fail("Failed to write the Workshop document because no workspace is loaded");
        }

        var resource = DocumentResource;

        var bookmarks = new List<WebViewBookmark>();
        foreach (var section in WorkshopSections.All)
        {
            // Names are resolved at seed time rather than when the bar is drawn, so a language change
            // reaches the bookmarks on the next workspace load.
            string name = _stringLocalizer.GetString(section.NameKey);

            bookmarks.Add(new WebViewBookmark(section.Url, name, section.IconName));
        }

        // The URL bar stays on: the sections link out to the wider web, and the user needs the way back.
        var content = new WebViewFileContent(WorkshopSections.Celbridge.Url)
        {
            Bookmarks = bookmarks
        };

        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        var writeResult = await resourceFileSystem.WriteAllTextAsync(resource, content.ToToml());
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to write the Workshop document: '{resource}'")
                .WithErrors(writeResult);
        }

        return Result.Ok();
    }
}
