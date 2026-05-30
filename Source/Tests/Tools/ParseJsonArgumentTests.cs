using System.Text.Json;
using System.Text.Json.Serialization;
using Celbridge.Tools;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for JsonArgumentParser — the helper that produces agent-shaped
/// failure messages from tool JSON deserialisation. Pins the success,
/// empty / null, unmapped-property, and malformed-JSON branches.
/// </summary>
[TestFixture]
public class ParseJsonArgumentTests
{
    // Matches the JsonSerializerOptions instance AgentToolBase exposes; using
    // the same shape here keeps the tests calibrated against what real tool
    // sites pass in.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    [Test]
    public void Parse_ValidJson_ReturnsTypedValue()
    {
        var result = JsonArgumentParser.Parse<List<SampleRecord>>("[{\"name\":\"a\",\"size\":1}]", "items JSON", Options);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].Name.Should().Be("a");
        result.Value[0].Size.Should().Be(1);
    }

    [Test]
    public void Parse_EmptyString_ReturnsRequiredFailure()
    {
        var result = JsonArgumentParser.Parse<List<SampleRecord>>("", "items JSON", Options);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Be("Invalid items JSON: argument is required.");
    }

    [Test]
    public void Parse_JsonLiteralNull_ReturnsNullFailure()
    {
        var result = JsonArgumentParser.Parse<List<SampleRecord>>("null", "items JSON", Options);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Be("Invalid items JSON: must be a non-null value.");
    }

    [Test]
    public void Parse_UnknownProperty_ListsValidPropertyNames()
    {
        var result = JsonArgumentParser.Parse<List<SampleRecord>>("[{\"name\":\"a\",\"unknownField\":1}]", "items JSON", Options);

        result.IsFailure.Should().BeTrue();
        var message = result.FirstErrorMessage;
        message.Should().StartWith("Invalid items JSON: unknown property 'unknownField'.");
        message.Should().Contain("Valid properties:");
        message.Should().Contain("name");
        message.Should().Contain("size");
        message.Should().Contain("with_meta");
    }

    [Test]
    public void Parse_UnknownProperty_HonoursJsonPropertyNameAttribute()
    {
        // The C# property IncludeMetadata is renamed in JSON to "with_meta"
        // by the attribute. Sending "includeMetadata" must therefore be
        // rejected as unknown, and the suggested list must show "with_meta"
        // rather than the C# property name.
        var result = JsonArgumentParser.Parse<List<SampleRecord>>("[{\"name\":\"a\",\"includeMetadata\":true}]", "items JSON", Options);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("with_meta");
        result.FirstErrorMessage.Should().NotContain("IncludeMetadata");
    }

    [Test]
    public void Parse_MalformedJson_ReturnsCleanedMessage()
    {
        var result = JsonArgumentParser.Parse<List<SampleRecord>>("not valid json", "items JSON", Options);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().StartWith("Invalid items JSON:");
        result.FirstErrorMessage.Should().NotContain("Celbridge.");
    }

    [Test]
    public void Parse_InternalNamespaceStrippedFromTypeNames()
    {
        // Force a JsonException by passing an object where an array is expected;
        // System.Text.Json includes the target type name in the message and the
        // parser strips any Celbridge. prefix from it.
        var result = JsonArgumentParser.Parse<List<SampleRecord>>("{}", "items JSON", Options);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().NotContain("Celbridge.");
        result.FirstErrorMessage.Should().NotContain("Celbridge.Tests");
    }

    public record SampleRecord(
        string Name,
        int Size,
        [property: JsonPropertyName("with_meta")] bool IncludeMetadata = false);
}
