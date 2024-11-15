using System.Text.Json.Nodes;
using Celbridge.Entities;

namespace Celbridge.Tests;

public class JsonUpdateTests
{
    [Test]
    public void Set_AddNewProperty_ShouldSucceed()
    {
        var json = JsonNode.Parse("{\"name\": \"John\"}")!.AsObject();
        var patcher = new JsonUpdate();

        patcher.Set("age", JsonValue.Create(30));
        var result = patcher.Apply(json);

        result.IsSuccess.Should().BeTrue();
        json["age"]!.GetValue<int>().Should().Be(30);
    }

    [Test]
    public void Set_ReplaceExistingProperty_ShouldSucceed()
    {
        var json = JsonNode.Parse("{\"name\": \"John\"}")!.AsObject();
        var patcher = new JsonUpdate();

        patcher.Set("name", JsonValue.Create("Jane"));
        var result = patcher.Apply(json);

        result.IsSuccess.Should().BeTrue();
        json["name"]!.GetValue<string>().Should().Be("Jane");
    }

    [Test]
    public void Remove_ExistingProperty_ShouldSucceed()
    {
        var json = JsonNode.Parse("{\"name\": \"John\", \"age\": 30}")!.AsObject();
        var patcher = new JsonUpdate();

        patcher.Remove("age");
        var result = patcher.Apply(json);

        result.IsSuccess.Should().BeTrue();
        json["age"].Should().BeNull();
    }

    [Test]
    public void Remove_NonExistentProperty_ShouldFail()
    {
        var json = JsonNode.Parse("{\"name\": \"John\"}")!.AsObject();
        var patcher = new JsonUpdate();

        patcher.Remove("age");
        var result = patcher.Apply(json);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void Move_PropertyToNewPath_ShouldSucceed()
    {
        var json = JsonNode.Parse("{\"name\": \"John\", \"address\": { \"street\": \"123 Main St\" } }")!.AsObject();
        var patcher = new JsonUpdate();

        patcher.Move("address.street", "street");
        var result = patcher.Apply(json);

        result.IsSuccess.Should().BeTrue();
        json["street"]!.GetValue<string>().Should().Be("123 Main St");
        json.SelectToken("address.street").Should().BeNull();
    }

    [Test]
    public void Move_NonExistentSourcePath_ShouldFail()
    {
        var json = JsonNode.Parse("{\"name\": \"John\"}")!.AsObject();
        var patcher = new JsonUpdate();

        patcher.Move("address.street", "street");
        var result = patcher.Apply(json);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void Copy_PropertyToNewPath_ShouldSucceed()
    {
        var json = JsonNode.Parse("{\"name\": \"John\", \"address\": { \"street\": \"123 Main St\" } }")!.AsObject();
        var patcher = new JsonUpdate();

        patcher.Copy("address.street", "street");
        var result = patcher.Apply(json);

        result.IsSuccess.Should().BeTrue();
        json["street"]!.GetValue<string>().Should().Be("123 Main St");
        json.SelectToken("address.street")!.GetValue<string>().Should().Be("123 Main St");
    }

    [Test]
    public void Copy_NonExistentSourcePath_ShouldFail()
    {
        var json = JsonNode.Parse("{\"name\": \"John\"}")!.AsObject();
        var patcher = new JsonUpdate();

        patcher.Copy("address.street", "street");
        var result = patcher.Apply(json);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void Test_PropertyMatches_ShouldSucceed()
    {
        var json = JsonNode.Parse("{\"name\": \"John\"}")!.AsObject();
        var patcher = new JsonUpdate();

        patcher.Test("name", JsonValue.Create("John"));
        var result = patcher.Apply(json);

        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public void Test_PropertyDoesNotMatch_ShouldFail()
    {
        var json = JsonNode.Parse("{\"name\": \"John\"}")!.AsObject();
        var patcher = new JsonUpdate();

        patcher.Test("name", JsonValue.Create("Jane"));
        var result = patcher.Apply(json);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void Apply_MultipleOperations_ShouldSucceed()
    {
        var json = JsonNode.Parse("{\"name\": \"John\"}")!.AsObject();
        var patcher = new JsonUpdate();

        patcher.Set("age", JsonValue.Create(30));
        patcher.Set("name", JsonValue.Create("Jane"));
        patcher.Remove("name");

        var result = patcher.Apply(json);

        result.IsSuccess.Should().BeTrue();
        json["age"]!.GetValue<int>().Should().Be(30);
        json["name"].Should().BeNull();
    }

    [Test]
    public void Undo_SetAndRemoveOperations_ShouldSucceed()
    {
        var json = JsonNode.Parse("{\"name\": \"John\"}")!.AsObject();
        var patcher = new JsonUpdate();

        patcher.Set("age", JsonValue.Create(30));
        patcher.Remove("name");

        patcher.Apply(json);
        var undoResult = patcher.Undo(json);

        undoResult.IsSuccess.Should().BeTrue();
        json["name"]!.GetValue<string>().Should().Be("John");
        json["age"].Should().BeNull();
    }

    [Test]
    public void Undo_MoveOperation_ShouldSucceed()
    {
        var json = JsonNode.Parse("{\"name\": \"John\", \"address\": { \"street\": \"123 Main St\" } }")!.AsObject();
        var patcher = new JsonUpdate();

        patcher.Move("address.street", "street");

        patcher.Apply(json);
        var undoResult = patcher.Undo(json);

        undoResult.IsSuccess.Should().BeTrue();
        json.SelectToken("address.street")!.GetValue<string>().Should().Be("123 Main St");
        json["street"].Should().BeNull();
    }

    [Test]
    public void Undo_CopyOperation_ShouldSucceed()
    {
        var json = JsonNode.Parse("{\"name\": \"John\", \"address\": { \"street\": \"123 Main St\" } }")!.AsObject();
        var patcher = new JsonUpdate();

        patcher.Copy("address.street", "street");

        patcher.Apply(json);
        var undoResult = patcher.Undo(json);

        undoResult.IsSuccess.Should().BeTrue();
        json["street"].Should().BeNull();
        json.SelectToken("address.street")!.GetValue<string>().Should().Be("123 Main St");
    }

    [Test]
    public void Undo_TestOperation_ShouldDoNothing()
    {
        var json = JsonNode.Parse("{\"name\": \"John\"}")!.AsObject();
        var patcher = new JsonUpdate();

        patcher.Test("name", JsonValue.Create("John"));

        patcher.Apply(json);
        var undoResult = patcher.Undo(json);

        undoResult.IsSuccess.Should().BeTrue();
        json["name"]!.GetValue<string>().Should().Be("John");
    }
}
