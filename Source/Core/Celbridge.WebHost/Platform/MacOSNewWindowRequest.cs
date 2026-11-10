namespace Celbridge.WebHost.Platform;

/// <summary>
/// A new window request from WebKit on the macOS head. WebKit leaves the page waiting until the request is answered.
/// </summary>
internal sealed class MacOSNewWindowRequest : WebNewWindowRequest
{
    // How long the new view may take to enter the visual tree before WebKit gets its answer. Uno reports a view's
    // navigations only after the view enters the visual tree. The page that asked stays paused until it gets the
    // answer.
    private static readonly TimeSpan LoadedTimeout = TimeSpan.FromSeconds(2);

    public MacOSNewWindowRequest(IntPtr nativeRequest, MacNewWindowAction action)
        : base(action.Url, action.IsUserInitiated)
    {
        NativeRequest = nativeRequest;
    }

    public IntPtr NativeRequest { get; }

    public override async Task AcceptAsync(IEditorWebView webView)
    {
        var macView = (MacOSWebView)webView;

        await macView.WaitForLoadedAsync(LoadedTimeout);

        if (!macView.TryGetNativeHandle(out var nativeHandle, out _))
        {
            Decline();
            return;
        }

        MacOSWebViewInterop.AnswerNewWindowRequest(NativeRequest, nativeHandle);
    }

    public override void Decline()
    {
        MacOSWebViewInterop.AnswerNewWindowRequest(NativeRequest, IntPtr.Zero);
    }
}
