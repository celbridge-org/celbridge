using Celbridge.Activities;
using Celbridge.Commands;
using Celbridge.Screenplay.Services;
using Celbridge.Workspace;

namespace Celbridge.Screenplay.Commands;

public class LoadScreenplayCommand : CommandBase
{
    private readonly IActivityService _activityService;

    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public ResourceKey WorkbookResource { get; set; } = ResourceKey.Empty;

    public LoadScreenplayCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _activityService = workspaceWrapper.WorkspaceService.ActivityService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var getActivityResult = _activityService.GetActivity(nameof(ScreenplayActivity));
        if (getActivityResult.IsFailure)
        {
            return Result.Fail($"Failed to get Screenplay activity")
                .WithErrors(getActivityResult);
        }

        var screenplayActivity = getActivityResult.Value as ScreenplayActivity;
        if (screenplayActivity is null)
        {
            return Result.Fail($"Activity is not a Screenplay activity")
                .WithErrors(getActivityResult);
        }

        var loadResult = await screenplayActivity.LoadScreenplayAsync(WorkbookResource);
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to load screenplay from workbook")
                .WithErrors(loadResult);
        }

        return Result.Ok();
    }
}
