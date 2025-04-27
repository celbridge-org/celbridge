using Celbridge.Commands;
using Celbridge.Screenplay.Services;
using Celbridge.Workspace;

namespace Celbridge.Screenplay.Commands;

public class SaveScreenplayCommand : CommandBase
{
    private readonly IServiceProvider _serviceProvider;

    public ResourceKey WorkbookResource { get; set; } = ResourceKey.Empty;

    public SaveScreenplayCommand(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var workspaceWrapper = _serviceProvider.AcquireService<IWorkspaceWrapper>();
        var activityService = workspaceWrapper.WorkspaceService.ActivityService;

        var getActivityResult = activityService.GetActivity(nameof(ScreenplayActivity));
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

        var saveResult = screenplayActivity.SaveScreenplay(WorkbookResource);
        if (saveResult.IsFailure)
        {
            return Result.Fail($"Failed to save screenplay to workbook")
                .WithErrors(saveResult);
        }

        await Task.CompletedTask;

        return Result.Ok();
    }
}
