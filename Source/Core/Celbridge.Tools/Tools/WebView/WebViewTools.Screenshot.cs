using System.Text.Json;
using Celbridge.Documents;
using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>
    /// Captures a screenshot of the WebView. By default the image is returned
    /// inline and not written to disk. Pass `saveTo` to also archive it as a
    /// project resource. The target document must be open and the active tab —
    /// call document_open first. Requires the webview-dev-tools feature flag.
    /// </summary>
    /// <param name="resource">Resource key of the open document whose WebView to capture.</param>
    /// <param name="saveTo">Where to save the captured image. Empty (default) skips the save. A trailing '/' or no extension is treated as a folder and an auto-named file is generated inside. A full key (e.g. "docs/output.png") writes to that resource. The extension must match `format`.</param>
    /// <param name="returnImage">When true (default) the response includes the image inline. Pass false for save-only flows to avoid inline tokens. At least one of returnImage or saveTo must produce output.</param>
    /// <param name="format">Image format: "jpeg" (default) or "png".</param>
    /// <param name="quality">JPEG quality, 1-100. Default 70. Ignored for PNG.</param>
    /// <param name="maxEdge">Maximum length of the longer image edge in pixels. Default 768. Bump to 1024 or higher to read fine on-screen text. Pass 0 to disable downscaling.</param>
    /// <param name="selector">Optional CSS selector. When supplied, clips the screenshot to the matched element. Empty (default) captures the viewport.</param>
    /// <param name="settleMs">Additional delay in milliseconds before the capture. Default 0. Bump to 500-1000 after layout-changing operations such as document_open if the editor signals content-ready before panels and async resources have settled.</param>
    /// <returns>Inline image (when returnImage is true) plus JSON metadata: `format`, `width`, `height`, `sizeBytes`, `resource` (the saved key, or null), `imageReturned`.</returns>
    [McpServerTool(Name = "webview_screenshot")]
    [ToolAlias("webview.screenshot")]
    public async partial Task<CallToolResult> Screenshot(
        string resource,
        string saveTo = "",
        bool returnImage = true,
        string format = "jpeg",
        int quality = 70,
        int maxEdge = 768,
        string selector = "",
        int settleMs = 0)
    {
        var webViewService = GetRequiredService<IWebViewService>();
        if (!webViewService.IsDevToolsFeatureEnabled())
        {
            return ErrorResult($"The '{FeatureFlagConstants.WebViewDevTools}' feature flag is disabled. Enable it in the user .celbridge config to use the webview_* tools.");
        }

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        var willSave = !string.IsNullOrEmpty(saveTo);
        if (!returnImage && !willSave)
        {
            return ErrorResult(
                "webview_screenshot was called with returnImage = false and no saveTo, " +
                "which would discard the captured image. Either set returnImage = true to view the image inline, " +
                "or provide a saveTo to archive it into the project tree.");
        }

        Logger.LogInformation("webview_screenshot resource={Resource} saveTo={SaveTo} returnImage={ReturnImage} format={Format} quality={Quality} maxEdge={MaxEdge} selector={Selector} settleMs={SettleMs}",
            resourceKey, saveTo, returnImage, format, quality, maxEdge, selector, settleMs);

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        // Resolve the save destination first when one was requested. Doing
        // this before the capture avoids wasting a screenshot when the
        // saveTo argument is malformed.
        ResourceKey? fileResource = null;
        if (willSave)
        {
            var projectFolderPath = resourceRegistry.ProjectFolderPath;
            if (string.IsNullOrEmpty(projectFolderPath))
            {
                return ErrorResult("No project is currently loaded. webview_screenshot requires an open project to resolve its save destination.");
            }

            var resolveResult = WebViewScreenshotResolver.Resolve(saveTo, format, projectFolderPath);
            if (resolveResult.IsFailure)
            {
                return ErrorResult(resolveResult.FirstErrorMessage);
            }
            fileResource = resolveResult.Value;
        }

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        var scopedSelector = string.IsNullOrEmpty(selector) ? null : selector;
        var clampedSettleMs = settleMs < 0 ? 0 : settleMs;
        var options = new ScreenshotOptions(format, quality, maxEdge, scopedSelector, clampedSettleMs);
        var screenshotResult = await toolBridge.ScreenshotAsync(resourceKey, options);
        if (screenshotResult.IsFailure)
        {
            return ErrorResult(screenshotResult.FirstErrorMessage);
        }

        var data = screenshotResult.Value;

        if (fileResource is not null)
        {
            // Route the write through IWriteBinaryDocumentCommand so capability
            // gating, registry refresh, and PathValidator containment all run.
            // The base64 round-trip is a one-time in-process cost. No network
            // or JSON envelope sees the encoded form, so the JSON-escape
            // corruption that drove the original on-disk redesign cannot recur.
            var base64 = Convert.ToBase64String(data.Bytes);
            var commandResult = await CommandService.ExecuteAsync<IWriteBinaryDocumentCommand>(command =>
            {
                command.FileResource = fileResource.Value;
                command.Base64Content = base64;
            });

            if (commandResult.IsFailure)
            {
                return ErrorResult($"Failed to save screenshot to resource '{fileResource}': {commandResult.FirstErrorMessage}");
            }
        }

        var response = new ScreenshotResponse(
            data.Format,
            data.Width,
            data.Height,
            data.Bytes.Length,
            fileResource?.ToString(),
            returnImage);

        var metadataJson = JsonSerializer.Serialize(response, JsonOptions);

        if (returnImage)
        {
            var mimeType = data.Format == "png" ? "image/png" : "image/jpeg";
            return SuccessResultWithImage(data.Bytes, mimeType, metadataJson);
        }

        return SuccessResult(metadataJson);
    }

    private sealed record ScreenshotResponse(
        string Format,
        int Width,
        int Height,
        int SizeBytes,
        string? Resource,
        bool ImageReturned);
}
