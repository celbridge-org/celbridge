using Celbridge.Commands;
using Celbridge.Localization;

namespace Celbridge.UserInterface.Commands;

public class SetLanguageCommand : CommandBase, ISetLanguageCommand
{
    private readonly ILanguageService _languageService;

    public string Language { get; set; } = string.Empty;

    public SetLanguageCommand(ILanguageService languageService)
    {
        _languageService = languageService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        _languageService.SetLanguage(Language);

        await Task.CompletedTask;

        return Result.Ok();
    }
}
