using Celbridge.Entities.Services;
using CommunityToolkit.Diagnostics;
using System.Text.Json.Nodes;

namespace Celbridge.Tests;

[TestFixture]
public class SchemaRegistryTests
{
    private EntitySchemaRegistry? _schemaRegistry;

    private string? _validSchemaJson;
    private string? _invalidSchemaJson;
    private string? _validDataJson;
    private string? _invalidDataJson;
    private string? _mismatchedEntityTypeDataJson;
    private string? _mismatchedEntityVersionDataJson;
    private string? _tempSchemaFilePath;

    [SetUp]
    public void SetUp()
    {
        _schemaRegistry = new EntitySchemaRegistry();

        // Sample valid schema with _entityType and _entityVersion
        _validSchemaJson = @"
        {
            ""_entityType"": ""TestSchema"",
            ""_entityVersion"": 1,
            ""type"": ""object"",
            ""properties"": {
                ""_entityType"": { ""type"": ""string"", ""const"": ""TestSchema"" },
                ""_entityVersion"": { ""type"": ""integer"", ""const"": 1 },
                ""name"": { ""type"": ""string"" },
                ""age"": { ""type"": ""integer"" }
            },
            ""required"": [""_entityType"", ""_entityVersion"", ""name"", ""age""]
        }";

        // Sample invalid schema without _entityType or _entityVersion
        _invalidSchemaJson = @"
        {
            ""type"": ""object"",
            ""properties"": {
                ""name"": { ""type"": ""string"" }
            }
        }";

        // Sample valid JSON data matching the schema
        _validDataJson = @"
        {
            ""_entityType"": ""TestSchema"",
            ""_entityVersion"": 1,
            ""name"": ""John Doe"",
            ""age"": 30
        }";

        // Sample JSON data with mismatched _entityType
        _mismatchedEntityTypeDataJson = @"
        {
            ""_entityType"": ""DifferentSchema"",
            ""_entityVersion"": 1,
            ""name"": ""John Doe"",
            ""age"": 30
        }";

        // Sample JSON data with mismatched _entityVersion
        _mismatchedEntityVersionDataJson = @"
        {
            ""_entityType"": ""TestSchema"",
            ""_entityVersion"": 2,
            ""name"": ""John Doe"",
            ""age"": 30
        }";

        // Temporary schema file for testing LoadSchemaFromFile
        _tempSchemaFilePath = Path.GetTempFileName();
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_tempSchemaFilePath))
        {
            File.Delete(_tempSchemaFilePath);
        }
    }

    [Test]
    public void AddSchema_WithValidSchema_ShouldReturnSuccess()
    {
        Guard.IsNotNull(_schemaRegistry);
        Guard.IsNotNull(_validSchemaJson);

        var result = _schemaRegistry.AddSchema(_validSchemaJson);
        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public void AddSchema_WithInvalidSchema_ShouldReturnFailure()
    {
        Guard.IsNotNull(_schemaRegistry);
        Guard.IsNotNull(_invalidSchemaJson);

        var result = _schemaRegistry.AddSchema(_invalidSchemaJson);
        result.IsSuccess.Should().BeFalse();
    }

    [Test]
    public void ValidateJsonNode_WithMatchingEntityTypeAndVersion_ShouldReturnSuccess()
    {
        Guard.IsNotNull(_schemaRegistry);
        Guard.IsNotNull(_validSchemaJson);
        Guard.IsNotNull(_validDataJson);

        _schemaRegistry.AddSchema(_validSchemaJson);

        var dataNode = JsonNode.Parse(_validDataJson) as JsonObject;
        Guard.IsNotNull(dataNode);

        var getSchema = _schemaRegistry.GetSchemaFromJson(_validDataJson);
        getSchema.IsSuccess.Should().BeTrue();
        var schema = getSchema.Value;

        var result = schema.ValidateJsonObject(dataNode);
        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public void ValidateJsonNode_WithMismatchedEntityType_ShouldReturnFailure()
    {
        Guard.IsNotNull(_schemaRegistry);
        Guard.IsNotNull(_mismatchedEntityTypeDataJson);
        Guard.IsNotNull(_validSchemaJson);

        _schemaRegistry.AddSchema(_validSchemaJson);
        var dataNode = JsonNode.Parse(_mismatchedEntityTypeDataJson) as JsonObject;
        Guard.IsNotNull(dataNode);

        var getSchema = _schemaRegistry.GetSchemaFromJson(_mismatchedEntityTypeDataJson);
        getSchema.IsSuccess.Should().BeFalse();
    }

    [Test]
    public void ValidateJsonNode_WithMismatchedEntityVersion_ShouldReturnFailure()
    {
        Guard.IsNotNull(_schemaRegistry);
        Guard.IsNotNull(_validSchemaJson);
        Guard.IsNotNull(_mismatchedEntityVersionDataJson);

        _schemaRegistry.AddSchema(_validSchemaJson);
        var dataNode = JsonNode.Parse(_mismatchedEntityVersionDataJson) as JsonObject;
        Guard.IsNotNull(dataNode);

        var getSchema = _schemaRegistry.GetSchemaFromJson(_mismatchedEntityVersionDataJson);
        getSchema.IsSuccess.Should().BeTrue();
        var schema = getSchema.Value;

        var result = schema.ValidateJsonObject(dataNode);
        result.IsSuccess.Should().BeFalse();
    }

    [Test]
    public void AddSchema_WithDuplicateEntityType_ShouldReturnFailure()
    {
        Guard.IsNotNull(_schemaRegistry);
        Guard.IsNotNull(_validSchemaJson);

        _schemaRegistry.AddSchema(_validSchemaJson);
        var result = _schemaRegistry.AddSchema(_validSchemaJson);
        result.IsSuccess.Should().BeFalse();
    }
}
