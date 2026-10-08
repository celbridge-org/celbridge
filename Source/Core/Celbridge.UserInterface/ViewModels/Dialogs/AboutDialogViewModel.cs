using Celbridge.Commands;
using Celbridge.Community;
using Celbridge.Platform;

namespace Celbridge.UserInterface.ViewModels;

public partial class AboutDialogViewModel : ObservableObject
{
    private readonly ICommandService _commandService;

    public string VersionText { get; }

    public AboutDialogViewModel(
        ICommandService commandService,
        IAppEnvironment appEnvironment,
        IStringLocalizer stringLocalizer)
    {
        _commandService = commandService;

        var appVersion = appEnvironment.GetEnvironmentInfo().AppVersion;
        VersionText = stringLocalizer.GetString("AboutDialog_Version", appVersion);
    }

    public void OpenWebsite()
    {
        OpenLink(CommunityUrls.Celbridge);
    }

    public void OpenGitHub()
    {
        OpenLink(CommunityUrls.GitHub);
    }

    private void OpenLink(string url)
    {
        // The open dialog holds the command queue, so the link opens outside it.
        _ = _commandService.ExecuteImmediate<IOpenBrowserCommand>(command => command.URL = url);
    }
}
