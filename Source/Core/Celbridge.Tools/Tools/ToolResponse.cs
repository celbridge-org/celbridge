using ModelContextProtocol.Protocol;

namespace Celbridge.Tools;

/// <summary>
/// Builds CallToolResult instances with consistent error capping. The single
/// home for tool-response shaping so the rules can be tightened (or audited)
/// in one place. New error categories that recur across tools belong here as
/// named factory methods that hardcode the right phrasing.
/// </summary>
public static class ToolResponse
{
    /// <summary>
    /// Maximum length, in characters, of an agent-visible error message. Long
    /// messages are truncated at the tail with an ellipsis so the outer-first
    /// wrapper survives. Guards against pathological exception messages from
    /// third-party libraries.
    /// </summary>
    private const int MaxErrorMessageLength = 1000;

    /// <summary>
    /// Creates a successful CallToolResult with a text message.
    /// </summary>
    public static CallToolResult Success(string text)
    {
        return new CallToolResult
        {
            Content = [
                new TextContentBlock
                {
                    Text = text
                }
            ]
        };
    }

    /// <summary>
    /// Creates an error CallToolResult that surfaces the given message,
    /// length-capped so pathological exception messages don't dominate the
    /// response. Every error path goes through this method so the cap and
    /// the IsError flag are applied uniformly.
    /// </summary>
    public static CallToolResult Error(string message)
    {
        var capped = CapErrorMessage(message);
        return new CallToolResult
        {
            IsError = true,
            Content = [
                new TextContentBlock
                {
                    Text = capped
                }
            ]
        };
    }

    /// <summary>
    /// Result-flavoured counterpart. Surfaces the failed Result's MessageChain
    /// so the agent sees the outer wrapper and any propagated inner causes.
    /// </summary>
    public static CallToolResult Error(Result result)
    {
        return Error(result.MessageChain);
    }

    /// <summary>
    /// Creates a successful CallToolResult that carries both an image and a
    /// JSON metadata text block. The image is delivered as a typed MCP image
    /// content block so the multimodal client decodes it into the model's
    /// vision context directly. The text block lets the agent reference
    /// metadata (size, format, saved location, etc.) alongside the image.
    /// </summary>
    public static CallToolResult SuccessWithImage(byte[] imageBytes, string mimeType, string metadataJson)
    {
        return new CallToolResult
        {
            Content = [
                ImageContentBlock.FromBytes(imageBytes, mimeType),
                new TextContentBlock
                {
                    Text = metadataJson
                }
            ]
        };
    }

    /// <summary>
    /// Standardised response for callers that pass a syntactically invalid
    /// resource key (forward slashes, no leading slash, no drive letters,
    /// etc.).
    /// </summary>
    public static CallToolResult InvalidResourceKey(string key) =>
        Error($"Invalid resource key: '{key}'.");

    /// <summary>
    /// Standardised response for tools whose feature flag is disabled.
    /// </summary>
    public static CallToolResult FeatureFlagDisabled(string flagName) =>
        Error($"The '{flagName}' feature flag is disabled. Enable it in the user .celbridge config to use this tool.");

    /// <summary>
    /// Standardised response for the "resource key parsed but the resource
    /// doesn't exist" case.
    /// </summary>
    public static CallToolResult ResourceNotFound(string resource) =>
        Error($"Resource not found: '{resource}'.");

    private static string CapErrorMessage(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= MaxErrorMessageLength)
        {
            return text;
        }

        const string ellipsis = "...";
        return string.Concat(text.AsSpan(0, MaxErrorMessageLength - ellipsis.Length), ellipsis);
    }
}
