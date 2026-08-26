using Celbridge.Commands;
using Celbridge.Dialog;

namespace Celbridge.UserInterface.Commands;

public class ShowSettingsCommand : CommandBase, IShowSettingsCommand
{
    public string SectionKey { get; set; } = string.Empty;

    public override async Task<Result> ExecuteAsync()
    {
        var dialogService = ServiceLocator.AcquireService<IDialogService>();
        await dialogService.ShowSettingsDialogAsync(SectionKey);

        return Result.Ok();
    }
}
