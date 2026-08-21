using Celbridge.Commands;
using Windows.System;

namespace Celbridge.UserInterface.Commands;

public class OpenBrowserCommand : CommandBase, IOpenBrowserCommand
{
    public string URL { get; set; } = string.Empty;

    public override async Task<Result> ExecuteAsync()
    {
        try
        {
            var targetUrl = URL.Trim();
            if (!string.IsNullOrWhiteSpace(targetUrl)
                && !targetUrl.StartsWith("http")
                && !targetUrl.StartsWith("file"))
            {
                targetUrl = $"https://{targetUrl}";
            }

            var uri = new Uri(targetUrl);
            await Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to open url in system default browser: {URL}")
                .WithException(ex);
        }

        return Result.Ok();
    }
}
