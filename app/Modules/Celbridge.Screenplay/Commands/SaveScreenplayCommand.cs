using Celbridge.Activities;
using Celbridge.Commands;
using Celbridge.Screenplay.Services;
using Celbridge.Workspace;

namespace Celbridge.Screenplay.Commands;

public class SaveScreenplayCommand : CommandBase
{
    private readonly IActivityService _activityService;

    public ResourceKey WorkbookResource { get; set; } = ResourceKey.Empty;

    public SaveScreenplayCommand(IWorkspaceWrapper workspaceWrapper)
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

        var saveResult = await screenplayActivity.SaveScreenplayAsync(WorkbookResource);
        if (saveResult.IsFailure)
        {
            return Result.Fail($"Failed to save screenplay to workbook")
                .WithErrors(saveResult);
        }

        return Result.Ok();
    }
}
