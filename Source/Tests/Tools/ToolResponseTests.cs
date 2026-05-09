using Celbridge.Tools;
using TextContentBlock = ModelContextProtocol.Protocol.TextContentBlock;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for ToolResponse — pins the literal-command guide-instruction format
/// agents see at the moment of failure, the category-helper messages, and the
/// shape of error and success CallToolResults.
/// </summary>
[TestFixture]
public class ToolResponseTests
{
    // FormatGuideInstruction

    [Test]
    public void FormatGuideInstruction_NoHook_RendersBareCall()
    {
        var instruction = ToolResponse.FormatGuideInstruction(new GuideReference("webview_click"));
        instruction.Should().Be("Run `guides_read([\"webview_click\"])`.");
    }

    [Test]
    public void FormatGuideInstruction_WithHook_AppendsHookAfterDash()
    {
        var instruction = ToolResponse.FormatGuideInstruction(
            new GuideReference("resource_keys", "forward-slash paths relative to the project content root"));
        instruction.Should().Be("Run `guides_read([\"resource_keys\"])` — forward-slash paths relative to the project content root.");
    }

    [Test]
    public void GuideReference_ImplicitConversionFromString_LeavesHookEmpty()
    {
        GuideReference reference = "webview_click";
        reference.Name.Should().Be("webview_click");
        reference.Hook.Should().BeEmpty();
    }

    // Error

    [Test]
    public void Error_AppendsGuideInstructionAfterMessage()
    {
        var result = ToolResponse.Error("webview_click requires a non-empty selector.", "webview_click");

        result.IsError.Should().BeTrue();
        var text = ((TextContentBlock)result.Content!.Single()).Text;
        text.Should().Be("webview_click requires a non-empty selector. Run `guides_read([\"webview_click\"])`.");
    }

    [Test]
    public void Error_FromFailedResult_UsesMessageChain()
    {
        var failedResult = Result.Fail("Outer wrapper");

        var result = ToolResponse.Error(failedResult, "file_read");

        result.IsError.Should().BeTrue();
        var text = ((TextContentBlock)result.Content!.Single()).Text;
        text.Should().Contain("Outer wrapper");
        text.Should().EndWith("Run `guides_read([\"file_read\"])`.");
    }

    // SuccessWithGuide

    [Test]
    public void SuccessWithGuide_PrimaryTextBlockUnchanged_GuideInstructionInSecondBlock()
    {
        var result = ToolResponse.SuccessWithGuide(
            "{\"totalMatches\":0,\"elements\":[]}",
            new GuideReference("webview_query", "zero matches — see selector, timing, and editor-binding notes"));

        result.IsError.Should().NotBe(true);
        result.Content.Should().HaveCount(2);
        var primary = ((TextContentBlock)result.Content![0]).Text;
        var secondary = ((TextContentBlock)result.Content![1]).Text;
        primary.Should().Be("{\"totalMatches\":0,\"elements\":[]}");
        secondary.Should().Be("Run `guides_read([\"webview_query\"])` — zero matches — see selector, timing, and editor-binding notes.");
    }

    // BootstrapError

    [Test]
    public void BootstrapError_DoesNotAppendGuideInstruction()
    {
        // Bootstrap tools (guides_*) document themselves; pointing them back at
        // guides_read on failure would be circular.
        var result = ToolResponse.BootstrapError("Unknown guide name 'banana'.");

        result.IsError.Should().BeTrue();
        var text = ((TextContentBlock)result.Content!.Single()).Text;
        text.Should().Be("Unknown guide name 'banana'.");
        text.Should().NotContain("guides_read");
    }

    // Category helpers

    [Test]
    public void InvalidResourceKey_PointsAtResourceKeysGuide()
    {
        var result = ToolResponse.InvalidResourceKey("Bad\\Key");

        result.IsError.Should().BeTrue();
        var text = ((TextContentBlock)result.Content!.Single()).Text;
        text.Should().StartWith("Invalid resource key: 'Bad\\Key'.");
        text.Should().Contain("`guides_read([\"resource_keys\"])`");
    }

    [Test]
    public void FeatureFlagDisabled_PointsAtNamespaceGuide()
    {
        var result = ToolResponse.FeatureFlagDisabled("webview-dev-tools", "webview");

        result.IsError.Should().BeTrue();
        var text = ((TextContentBlock)result.Content!.Single()).Text;
        text.Should().StartWith("The 'webview-dev-tools' feature flag is disabled.");
        text.Should().Contain("`guides_read([\"webview\"])`");
        text.Should().Contain("feature flag setup and prerequisites");
    }

    [Test]
    public void ResourceNotFound_PointsAtPerToolGuide()
    {
        var result = ToolResponse.ResourceNotFound("Scripts/missing.py", "file_read");

        result.IsError.Should().BeTrue();
        var text = ((TextContentBlock)result.Content!.Single()).Text;
        text.Should().StartWith("Resource not found: 'Scripts/missing.py'.");
        text.Should().Contain("`guides_read([\"file_read\"])`");
        text.Should().Contain("verify the resource exists before targeting it");
    }
}
