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
            // A bare host name is taken as a web address. A mail address goes to the system's mail app as it is.
            var targetUrl = URL.Trim();
            if (!string.IsNullOrWhiteSpace(targetUrl)
                && !targetUrl.StartsWith("http")
                && !targetUrl.StartsWith("file")
                && !targetUrl.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
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
