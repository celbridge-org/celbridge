using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace Celbridge.Tools;

/// <summary>
/// Builds CallToolResult instances with consistent error capping. The single
/// home for tool-response shaping so the rules can be tightened (or audited)
/// in one place. New error categories that recur across tools belong here as
/// named factory methods that hardcode the right phrasing. Category helpers
/// that map to a troubleshooter guide stash the troubleshooter name in
/// CallToolResult.Meta under TroubleshooterMetaKey; AgentResponseFilter reads
/// the entry, removes it, and adds the named troubleshooter to its
/// auto-attach candidate list before the response leaves the broker.
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
    /// Meta-dictionary key under which a category helper records the
    /// troubleshooter guide that should auto-attach. AgentResponseFilter
    /// reads and removes this entry before the response leaves the broker,
    /// so the agent never sees it.
    /// </summary>
    public const string TroubleshooterMetaKey = "celbridge.troubleshooter";

    /// <summary>
    /// Maps each category-helper method name to the troubleshooter guide
    /// auto-attached the first time it fires in a session. Used by the guide
    /// loader to validate that every helper-declared troubleshooter has a
    /// loaded guide and every loaded troubleshooter is referenced by a helper.
    /// </summary>
    public static IReadOnlyDictionary<string, string> HelperTroubleshooters { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(InvalidResourceKey)] = "troubleshoot_resource_key",
            [nameof(FeatureFlagDisabled)] = "troubleshoot_feature_flag",
            [nameof(ResourceNotFound)] = "troubleshoot_resource_not_found",
            [nameof(SpotlightTargetNotFound)] = "troubleshoot_spotlight_target",
        };

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
        ErrorWithTroubleshooter(
            $"Invalid resource key: '{key}'.",
            HelperTroubleshooters[nameof(InvalidResourceKey)]);

    /// <summary>
    /// Standardised response for tools whose feature flag is disabled.
    /// </summary>
    public static CallToolResult FeatureFlagDisabled(string flagName) =>
        ErrorWithTroubleshooter(
            $"The '{flagName}' feature flag is disabled. Enable it in the user .celbridge config to use this tool.",
            HelperTroubleshooters[nameof(FeatureFlagDisabled)]);

    /// <summary>
    /// Standardised response for the "resource key parsed but the resource
    /// doesn't exist" case.
    /// </summary>
    public static CallToolResult ResourceNotFound(string resource) =>
        ErrorWithTroubleshooter(
            $"Resource not found: '{resource}'.",
            HelperTroubleshooters[nameof(ResourceNotFound)]);

    /// <summary>
    /// Standardised response for app_spotlight when the requested target is not a
    /// catalogued landmark. Lists the valid landmark identifiers.
    /// </summary>
    public static CallToolResult SpotlightTargetNotFound(string target, string validTargets) =>
        ErrorWithTroubleshooter(
            $"Unknown spotlight target: '{target}'. Valid landmarks: {validTargets}.",
            HelperTroubleshooters[nameof(SpotlightTargetNotFound)]);

    private static CallToolResult ErrorWithTroubleshooter(string message, string troubleshooterName)
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
            ],
            Meta = new JsonObject
            {
                [TroubleshooterMetaKey] = troubleshooterName
            }
        };
    }

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
