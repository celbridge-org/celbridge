using ModelContextProtocol.Protocol;

namespace Celbridge.Tools;

/// <summary>
/// Pairs a guide name with a one-clause hook explaining why it is relevant to
/// the current failure or documented gotcha. Surfaced in tool responses so the
/// agent gets a concrete next step at the moment of failure rather than the
/// generic guide-nudge suffix.
/// </summary>
public readonly record struct GuidePointer(string Name, string Hook);

/// <summary>
/// Builds CallToolResult instances with consistent error capping, guide-nudge,
/// and guide-pointer policy. The single home for tool-response shaping so the
/// rules can be tightened (or audited) in one place. New error categories that
/// recur across tools belong here as named factory methods.
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
    /// Appended to every agent-visible error message produced by Error so the
    /// agent knows where to find the tool's full guide. Bootstrap tools that
    /// document themselves (guides_*) opt out via BootstrapError.
    /// </summary>
    private const string GuideNudgeSuffix =
        " If this tool is unfamiliar, call `guides_read` with the tool name to fetch its full guide.";

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
    /// Creates an error CallToolResult with a text message. The generic
    /// guide-nudge suffix is appended so the agent knows it can fetch the
    /// tool's full guide via guides_read on a recoverable failure. Bootstrap
    /// tools that document themselves use BootstrapError instead.
    /// </summary>
    public static CallToolResult Error(string text)
    {
        var withNudge = AppendGuideNudge(text);
        var capped = CapErrorMessage(withNudge);
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
    /// Creates an error CallToolResult from a failed Result, surfacing its
    /// MessageChain so the agent sees the outer wrapper and any propagated
    /// inner causes. Adds the guide-nudge suffix.
    /// </summary>
    public static CallToolResult Error(Result result)
    {
        return Error(result.MessageChain);
    }

    /// <summary>
    /// Error variant that surfaces specific guide pointers in place of the
    /// generic guide-nudge suffix. Use when the failure path knows which guide
    /// covers the gotcha; the bare Error stays the right call when the failure
    /// is generic (bad input, transient infrastructure error). Falls back to
    /// the generic nudge if pointers is empty so callers can pass a computed
    /// array without a length guard.
    /// </summary>
    public static CallToolResult Error(string text, params GuidePointer[] pointers)
    {
        if (pointers.Length == 0)
        {
            return Error(text);
        }

        var withPointers = AppendGuidePointers(text, pointers);
        var capped = CapErrorMessage(withPointers);
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
    /// Result-flavoured counterpart of the pointers overload. Surfaces the
    /// failed Result's MessageChain alongside the specific guide pointers.
    /// </summary>
    public static CallToolResult Error(Result result, params GuidePointer[] pointers)
    {
        return Error(result.MessageChain, pointers);
    }

    /// <summary>
    /// Bootstrap-tool error variant. Does not append the guide-nudge suffix —
    /// the bootstrap tools (guides_list, guides_read, guides_search) are how
    /// the agent reads guides in the first place, so nudging them at
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
    /// Successful result that also carries guide pointers as a secondary text
    /// block. Use for documented gotcha cases where the call technically
    /// succeeded but the agent is likely stuck (an empty query result with no
    /// matches is the canonical case). The primary text block stays the
    /// unmodified value so programmatic consumers reading only the first block
    /// keep working; the secondary block delivers the hint.
    /// </summary>
    public static CallToolResult SuccessWithGuides(string text, params GuidePointer[] pointers)
    {
        if (pointers.Length == 0)
        {
            return Success(text);
        }

        var pointerText = FormatGuidePointers(pointers);
        return new CallToolResult
        {
            Content = [
                new TextContentBlock
                {
                    Text = text
                },
                new TextContentBlock
                {
                    Text = pointerText
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
    /// Formats a non-empty pointer list as a human-readable suffix. Visible
    /// to the test assembly so the format can be pinned without driving a
    /// real tool through its bridge.
    /// </summary>
    internal static string FormatGuidePointers(IReadOnlyList<GuidePointer> pointers)
    {
        if (pointers.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>(pointers.Count);
        foreach (var pointer in pointers)
        {
            parts.Add($"`{pointer.Name}` ({pointer.Hook})");
        }

        var joined = string.Join(", ", parts);
        return $"Related guides: {joined}. Fetch via `guides_read`.";
    }

    private static string AppendGuideNudge(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return GuideNudgeSuffix.TrimStart();
        }

        // Trailing whitespace and punctuation already on the message stay as
        // written; the suffix has its own leading space.
        return text + GuideNudgeSuffix;
    }

    private static string AppendGuidePointers(string text, IReadOnlyList<GuidePointer> pointers)
    {
        var pointerText = FormatGuidePointers(pointers);
        if (string.IsNullOrEmpty(text))
        {
            return pointerText;
        }

        return text + " " + pointerText;
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
