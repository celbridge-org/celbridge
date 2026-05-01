using Celbridge.Commands;

namespace Celbridge.WebHost.Commands;

public class GetWebViewToolSupportCommand : CommandBase, IGetWebViewToolSupportCommand
{
    private readonly IWebViewService _webViewService;

    public override CommandFlags CommandFlags => CommandFlags.SuppressCommandLog;

    public ResourceKey Resource { get; set; } = ResourceKey.Empty;

    public WebViewToolSupport ResultValue { get; private set; }
        = new WebViewToolSupport(IsSupported: true, Reason: null);

    public GetWebViewToolSupportCommand(IWebViewService webViewService)
    {
        _webViewService = webViewService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        ResultValue = _webViewService.GetWebViewToolSupport(Resource);
        return Result.Ok();
    }
}
