using System.Text.Json.Nodes;
using Celbridge.Server.Services;

namespace Celbridge.Tests;

/// <summary>
/// Tests for McpToolBridge.ReadSchemaType. The MCP SDK emits a JSON Schema
/// `"type"` as either a literal string or an array (e.g. "[\"integer\", \"null\"]"
/// for a nullable value-type parameter). Earlier code called
/// JsonNode.GetValue&lt;string&gt;() unconditionally and threw
/// InvalidOperationException on the array shape, crashing tool registration.
/// </summary>
[TestFixture]
public class McpToolBridgeReadSchemaTypeTests
{
    [Test]
    public void ReadSchemaType_NullNode_ReturnsNull()
    {
        var result = McpToolBridge.ReadSchemaType(null);

        result.Should().BeNull();
    }

    [Test]
    public void ReadSchemaType_StringNode_ReturnsValue()
    {
        var node = JsonNode.Parse("\"integer\"");

        var result = McpToolBridge.ReadSchemaType(node);

        result.Should().Be("integer");
    }

    [Test]
    public void ReadSchemaType_ArrayWithIntegerAndNull_ReturnsInteger()
    {
        var node = JsonNode.Parse("[\"integer\", \"null\"]");

        var result = McpToolBridge.ReadSchemaType(node);

        result.Should().Be("integer");
    }

    [Test]
    public void ReadSchemaType_ArrayWithNullFirst_StillReturnsNonNullEntry()
    {
        var node = JsonNode.Parse("[\"null\", \"string\"]");

        var result = McpToolBridge.ReadSchemaType(node);

        result.Should().Be("string");
    }

    [Test]
    public void ReadSchemaType_ArrayWithOnlyNull_ReturnsNull()
    {
        var node = JsonNode.Parse("[\"null\"]");

        var result = McpToolBridge.ReadSchemaType(node);

        result.Should().BeNull();
    }

    [Test]
    public void ReadSchemaType_EmptyArray_ReturnsNull()
    {
        var node = JsonNode.Parse("[]");

        var result = McpToolBridge.ReadSchemaType(node);

        result.Should().BeNull();
    }
}
