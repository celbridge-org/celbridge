using Celbridge.Commands;
using Celbridge.Logging;

namespace Celbridge.WebHost.Commands;

public class ClearBrowsingDataCommand : CommandBase, IClearBrowsingDataCommand
{
    private readonly ILogger<ClearBrowsingDataCommand> _logger;
    private readonly IWebViewPlatform _webViewPlatform;

    public ClearBrowsingDataCommand(
        ILogger<ClearBrowsingDataCommand> logger,
        IWebViewPlatform webViewPlatform)
    {
        _logger = logger;
        _webViewPlatform = webViewPlatform;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_webViewPlatform.SupportsLiveBrowsingDataClear)
        {
            return Result.Fail("Clearing browsing data is not supported on this platform");
        }

        try
        {
            await _webViewPlatform.ClearBrowsingDataAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear the browsing data");
            return Result.Fail("Failed to clear the browsing data").WithException(ex);
        }

        return Result.Ok();
    }
}
