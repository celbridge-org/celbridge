using System.Text.Json.Nodes;
using Celbridge.Server.Services;

namespace Celbridge.Tests.Server;

/// <summary>
/// Tests for McpToolBridge.BuildToolParameters, the single parameter-extraction
/// path shared by ListToolsAsync and the tools/list JSON-RPC method. A nullable
/// value-type parameter (e.g. int?) serializes its schema "type" as a JSON array
/// (["integer", "null"]); the extraction must read that without throwing, which is
/// what keeps the cel proxy's tools/list from crashing for every tool.
/// </summary>
[TestFixture]
public class McpToolBridgeBuildToolParametersTests
{
    [Test]
    public void NullableValueTypeParameter_ResolvesToUnderlyingType()
    {
        var inputSchema = JsonNode.Parse("""
            {
              "properties": {
                "target": { "type": "string", "description": "Landmark id" },
                "durationMs": { "type": ["integer", "null"], "description": "Auto-clear delay" }
              },
              "required": ["target"]
            }
            """);

        var parameters = McpToolBridge.BuildToolParameters(inputSchema);

        var durationParameter = parameters.Single(parameter => parameter.Name == "durationMs");
        durationParameter.Type.Should().Be("integer");
        durationParameter.HasDefaultValue.Should().BeTrue();

        var targetParameter = parameters.Single(parameter => parameter.Name == "target");
        targetParameter.Type.Should().Be("string");
        targetParameter.HasDefaultValue.Should().BeFalse();
    }

    [Test]
    public void MissingInputSchema_ReturnsEmpty()
    {
        var parameters = McpToolBridge.BuildToolParameters(null);

        parameters.Should().BeEmpty();
    }

    [Test]
    public void ArrayParameter_CapturesItemType()
    {
        var inputSchema = JsonNode.Parse("""
            {
              "properties": {
                "resources": { "type": "array", "items": { "type": "string" } }
              },
              "required": ["resources"]
            }
            """);

        var parameters = McpToolBridge.BuildToolParameters(inputSchema);

        var resourcesParameter = parameters.Single(parameter => parameter.Name == "resources");
        resourcesParameter.Type.Should().Be("array");
        resourcesParameter.ItemType.Should().Be("string");
    }
}
