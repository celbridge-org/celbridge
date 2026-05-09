using ModelContextProtocol.Protocol;

namespace Celbridge.Tools;

/// <summary>
/// Names a guide and an optional one-clause hook explaining why it is relevant.
/// Surfaced in tool responses as a literal `guides_read(["name"])` call so the
/// agent has a copy-pasteable next step at the moment of failure rather than a
/// prose suggestion. Implicit conversion from string lets call sites pass just
/// the guide name when the hook is empty: `Error(msg, "webview_click")`.
/// </summary>
public readonly record struct GuideReference(string Name, string Hook = "")
{
    public static implicit operator GuideReference(string name) => new(name);
}

/// <summary>
/// Builds CallToolResult instances with consistent error capping and guide-
/// reference policy. The single home for tool-response shaping so the rules
/// can be tightened (or audited) in one place. Every error response carries a
/// guide reference; new error categories that recur across tools belong here
/// as named factory methods that hardcode the right pointer.
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
    /// Creates an error CallToolResult that surfaces the given message and a
    /// literal guides_read invocation pointing at the supplied guide. Every
    /// error path goes through this method so every agent-visible failure
    /// names a concrete next step.
    /// </summary>
    public static CallToolResult Error(string message, GuideReference guide)
    {
        var formatted = AppendGuideInstruction(message, guide);
        var capped = CapErrorMessage(formatted);
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
    /// so the agent sees the outer wrapper and any propagated inner causes,
    /// alongside the literal guides_read invocation.
    /// </summary>
    public static CallToolResult Error(Result result, GuideReference guide)
    {
        return Error(result.MessageChain, guide);
    }

    /// <summary>
    /// Bootstrap-tool error variant. Does not append a guide instruction —
    /// the bootstrap tools (guides_list, guides_read, guides_search) are how
    /// the agent reads guides in the first place, so referring them back at
    /// guides_read would be circular.
    /// </summary>
    public static CallToolResult BootstrapError(string text)
    {
        var capped = CapErrorMessage(text);
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
    /// Successful result that also carries a guide reference as a secondary
    /// text block. Use for documented gotcha cases where the call technically
    /// succeeded but the agent is likely stuck (zero-match query results are
    /// the canonical case). The primary text block stays the unmodified value
    /// so programmatic consumers reading only the first block keep working;
    /// the secondary block delivers the literal guides_read invocation.
    /// </summary>
    public static CallToolResult SuccessWithGuide(string text, GuideReference guide)
    {
        var instruction = FormatGuideInstruction(guide);
        return new CallToolResult
        {
            Content = [
                new TextContentBlock
                {
                    Text = text
                },
                new TextContentBlock
                {
                    Text = instruction
                }
            ]
        };
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
    /// etc.). Always points at the resource_keys concept guide.
    /// </summary>
    public static CallToolResult InvalidResourceKey(string key) =>
        Error(
            $"Invalid resource key: '{key}'.",
            new GuideReference("resource_keys", "forward-slash paths relative to the project content root, never backslashes or absolute"));

    /// <summary>
    /// Standardised response for tools whose feature flag is disabled. The
    /// caller supplies the namespace name so the pointer lands at the
    /// namespace guide that documents the flag setup.
    /// </summary>
    public static CallToolResult FeatureFlagDisabled(string flagName, string namespaceName) =>
        Error(
            $"The '{flagName}' feature flag is disabled. Enable it in the user .celbridge config to use this tool.",
            new GuideReference(namespaceName, "feature flag setup and prerequisites"));

    /// <summary>
    /// Standardised response for the "resource key parsed but the resource
    /// doesn't exist" case. The caller supplies the per-tool guide name so
    /// the pointer lands on the tool the agent was using when they hit the
    /// missing resource.
    /// </summary>
    public static CallToolResult ResourceNotFound(string resource, string toolName) =>
        Error(
            $"Resource not found: '{resource}'.",
            new GuideReference(toolName, "verify the resource exists before targeting it"));

    /// <summary>
    /// Formats a guide reference as a literal `guides_read(["name"])` call
    /// for the agent to copy-paste. Visible to the test assembly so the
    /// format can be pinned without driving a real tool through its bridge.
    /// </summary>
    internal static string FormatGuideInstruction(GuideReference guide)
    {
        if (string.IsNullOrEmpty(guide.Hook))
        {
            return $"Run `guides_read([\"{guide.Name}\"])`.";
        }

        return $"Run `guides_read([\"{guide.Name}\"])` — {guide.Hook}.";
    }

    private static string AppendGuideInstruction(string message, GuideReference guide)
    {
        var instruction = FormatGuideInstruction(guide);
        if (string.IsNullOrEmpty(message))
        {
            return instruction;
        }

        return message + " " + instruction;
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
