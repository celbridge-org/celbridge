using Celbridge.Tools;
using TextContentBlock = ModelContextProtocol.Protocol.TextContentBlock;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for ToolResponse — pins the category-helper messages, the length cap,
/// and the shape of error and success CallToolResults.
/// </summary>
[TestFixture]
public class ToolResponseTests
{
    // Success / Error

    [Test]
    public void Success_PutsTextInSingleBlock_AndIsNotMarkedAsError()
    {
        var result = ToolResponse.Success("payload");

        result.IsError.Should().NotBe(true);
        var text = ((TextContentBlock)result.Content!.Single()).Text;
        text.Should().Be("payload");
    }

    [Test]
    public void Error_MessageGoesIntoSingleBlock_AndIsMarkedAsError()
    {
        var result = ToolResponse.Error("webview_click requires a non-empty selector.");

        result.IsError.Should().BeTrue();
        var text = ((TextContentBlock)result.Content!.Single()).Text;
        text.Should().Be("webview_click requires a non-empty selector.");
    }

    [Test]
    public void Error_FromFailedResult_UsesMessageChain()
    {
        var failedResult = Result.Fail("Outer wrapper").WithErrors(Result.Fail("Inner cause"));

        var result = ToolResponse.Error(failedResult);

        result.IsError.Should().BeTrue();
        var text = ((TextContentBlock)result.Content!.Single()).Text;
        text.Should().Contain("Outer wrapper");
        text.Should().Contain("Inner cause");
    }

    [Test]
    public void Error_LongMessage_IsCappedWithEllipsis()
    {
        var longMessage = new string('a', 2_000);

        var result = ToolResponse.Error(longMessage);

        var text = ((TextContentBlock)result.Content!.Single()).Text;
        text.Length.Should().Be(1_000);
        text.Should().EndWith("...");
    }

    // Category helpers — each emits the error message in a single content block
    // and stashes the troubleshooter name in Meta for AgentResponseFilter to
    // consume.

    [Test]
    public void InvalidResourceKey_RendersTheBadKeyAndStashesTroubleshooterInMeta()
    {
        var result = ToolResponse.InvalidResourceKey("Bad\\Key");

        result.IsError.Should().BeTrue();
        var text = ((TextContentBlock)result.Content!.Single()).Text;
        text.Should().Be("Invalid resource key: 'Bad\\Key'.");
        result.Meta![ToolResponse.TroubleshooterMetaKey]!.GetValue<string>()
            .Should().Be("troubleshoot_resource_key");
    }

    [Test]
    public void FeatureFlagDisabled_NamesTheFlagAndStashesTroubleshooterInMeta()
    {
        var result = ToolResponse.FeatureFlagDisabled("webview-dev-tools");

        result.IsError.Should().BeTrue();
        var text = ((TextContentBlock)result.Content!.Single()).Text;
        text.Should().Be("The 'webview-dev-tools' feature flag is disabled. Enable it in the user .celbridge config to use this tool.");
        result.Meta![ToolResponse.TroubleshooterMetaKey]!.GetValue<string>()
            .Should().Be("troubleshoot_feature_flag");
    }

    [Test]
    public void ResourceNotFound_RendersTheMissingResourceAndStashesTroubleshooterInMeta()
    {
        var result = ToolResponse.ResourceNotFound("Scripts/missing.py");

        result.IsError.Should().BeTrue();
        var text = ((TextContentBlock)result.Content!.Single()).Text;
        text.Should().Be("Resource not found: 'Scripts/missing.py'.");
        result.Meta![ToolResponse.TroubleshooterMetaKey]!.GetValue<string>()
            .Should().Be("troubleshoot_resource_not_found");
    }

    [Test]
    public void HelperTroubleshooters_NamesEveryCategoryHelper()
    {
        // The set of helper-to-troubleshooter pairs is what the guide loader
        // validates against the loaded troubleshooter guides. Pin the contract.
        ToolResponse.HelperTroubleshooters.Keys.Should().BeEquivalentTo(
            "InvalidResourceKey",
            "FeatureFlagDisabled",
            "ResourceNotFound");
        ToolResponse.HelperTroubleshooters.Values.Should().BeEquivalentTo(
            "troubleshoot_resource_key",
            "troubleshoot_feature_flag",
            "troubleshoot_resource_not_found");
    }
}
