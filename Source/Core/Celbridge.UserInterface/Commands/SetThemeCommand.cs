using Celbridge.Commands;
using Celbridge.Settings;

namespace Celbridge.UserInterface.Commands;

public class SetThemeCommand : CommandBase, ISetThemeCommand
{
    private readonly IUserInterfaceService _userInterfaceService;

    public ApplicationColorTheme Theme { get; set; }

    public SetThemeCommand(IUserInterfaceService userInterfaceService)
    {
        _userInterfaceService = userInterfaceService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        _userInterfaceService.SetTheme(Theme);

        await Task.CompletedTask;

        return Result.Ok();
    }
}
