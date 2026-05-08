using System.Text.Json;
using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>Capture a visual screenshot of the WebView (returned inline and/or saved into the project tree).</summary>
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
            return ToolError($"The '{FeatureFlagConstants.WebViewDevTools}' feature flag is disabled. Enable it in the user .celbridge config to use the webview_* tools.");
        }

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolError($"Invalid resource key: '{resource}'");
        }

        var willSave = !string.IsNullOrEmpty(saveTo);
        if (!returnImage && !willSave)
        {
            return ToolError(
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
                return ToolError("No project is currently loaded. webview_screenshot requires an open project to resolve its save destination.");
            }

            var resolveResult = WebViewScreenshotResolver.Resolve(saveTo, format, projectFolderPath);
            if (resolveResult.IsFailure)
            {
                return ToolError(resolveResult);
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
            return ToolError(screenshotResult);
        }

        var data = screenshotResult.Value;

        if (fileResource is not null)
        {
            // Route the write through IWriteBinaryFileCommand so capability
            // gating, registry refresh, and PathValidator containment all run.
            // The base64 round-trip is a one-time in-process cost. No network
            // or JSON envelope sees the encoded form, so the JSON-escape
            // corruption that drove the original on-disk redesign cannot recur.
            var base64 = Convert.ToBase64String(data.Bytes);
            var commandResult = await CommandService.ExecuteAsync<IWriteBinaryFileCommand>(command =>
            {
                command.FileResource = fileResource.Value;
                command.Base64Content = base64;
            });

            if (commandResult.IsFailure)
            {
                var failure = Result.Fail($"Failed to save screenshot to resource '{fileResource}'")
                    .WithErrors(commandResult);
                return ToolError(failure);
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
            return ToolSuccessWithImage(data.Bytes, mimeType, metadataJson);
        }

        return ToolSuccess(metadataJson);
    }

    private sealed record ScreenshotResponse(
        string Format,
        int Width,
        int Height,
        int SizeBytes,
        string? Resource,
        bool ImageReturned);
}
