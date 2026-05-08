using Celbridge.Tools;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for the guide-pointer formatter on ToolResponse. The formatter is
/// the substring that lands in tool error and gotcha-success responses, so
/// the format is what agents see at the moment of failure.
/// </summary>
[TestFixture]
public class ToolResponseTests
{
    [Test]
    public void FormatGuidePointers_EmptyListReturnsEmptyString()
    {
        var formatted = ToolResponse.FormatGuidePointers(Array.Empty<GuidePointer>());
        formatted.Should().BeEmpty();
    }

    [Test]
    public void FormatGuidePointers_SinglePointerWrapsNameInBackticks()
    {
        var pointers = new[]
        {
            new GuidePointer("webview", "canvas-painted UI cannot be queried with DOM selectors"),
        };

        var formatted = ToolResponse.FormatGuidePointers(pointers);

        formatted.Should().Be("Related guides: `webview` (canvas-painted UI cannot be queried with DOM selectors). Fetch via `guides_read`.");
    }

    [Test]
    public void FormatGuidePointers_MultiplePointersJoinWithCommas()
    {
        var pointers = new[]
        {
            new GuidePointer("webview", "canvas interaction, selector limits"),
            new GuidePointer("webview_devtools", "content-ready timing"),
        };

        var formatted = ToolResponse.FormatGuidePointers(pointers);

        formatted.Should().Be("Related guides: `webview` (canvas interaction, selector limits), `webview_devtools` (content-ready timing). Fetch via `guides_read`.");
    }
}
