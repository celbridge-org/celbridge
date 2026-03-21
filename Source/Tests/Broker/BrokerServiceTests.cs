using Celbridge.Broker;
using Celbridge.Broker.Services;
using Microsoft.Extensions.Logging;

namespace Celbridge.Tests;

// Test tool methods for BrokerService and ToolExecutor tests.
// These are separate from ToolRegistryTests tools to keep concerns clear.

public static class ExecutorTestTools
{
    public static bool VoidToolWasCalled { get; set; }

    [McpTool(Name = "exec/void", Alias = "void_tool", Description = "A void tool")]
    public static void VoidTool()
    {
        VoidToolWasCalled = true;
    }

    [McpTool(Name = "exec/greet", Alias = "greet", Description = "Returns a greeting")]
    public static string Greet(
        [McpParam(Description = "Name to greet")]
        string name)
    {
        return $"Hello, {name}!";
    }

    [McpTool(Name = "exec/add", Alias = "add", Description = "Adds two numbers")]
    public static int Add(
        [McpParam(Description = "First number")]
        int left,
        [McpParam(Description = "Second number")]
        int right)
    {
        return left + right;
    }

    [McpTool(Name = "exec/optional", Alias = "optional", Description = "Tool with optional parameter")]
    public static string OptionalParams(
        [McpParam(Description = "Required value")]
        string value,
        [McpParam(Description = "Optional suffix")]
        string suffix = "!")
    {
        return value + suffix;
    }

    [McpTool(Name = "exec/async_ok", Alias = "async_ok", Description = "Async tool that succeeds")]
    public static async Task<Result> AsyncOk()
    {
        await Task.CompletedTask;
        return Result.Ok();
    }

    [McpTool(Name = "exec/async_fail", Alias = "async_fail", Description = "Async tool that fails")]
    public static async Task<Result> AsyncFail()
    {
        await Task.CompletedTask;
        return Result.Fail("Something went wrong");
    }

    [McpTool(Name = "exec/async_value", Alias = "async_value", Description = "Async tool returning a value")]
    public static async Task<Result<string>> AsyncValue(
        [McpParam(Description = "Input text")]
        string text)
    {
        await Task.CompletedTask;
        var upperText = text.ToUpperInvariant();
        return Result<string>.Ok(upperText);
    }

    [McpTool(Name = "exec/throws", Alias = "throws", Description = "Tool that throws")]
    public static string Throws()
    {
        throw new InvalidOperationException("Deliberate test exception");
    }

    [McpTool(Name = "exec/resource_key", Alias = "resource_key", Description = "Tool with ResourceKey parameter")]
    public static string ResourceKeyTool(
        [McpParam(Description = "A resource key")]
        ResourceKey key)
    {
        return key.ToString();
    }

    [McpTool(Name = "exec/bool_param", Alias = "bool_param", Description = "Tool with bool parameter")]
    public static string BoolTool(
        [McpParam(Description = "A flag")]
        bool enabled)
    {
        return enabled ? "on" : "off";
    }
}

[TestFixture]
public class BrokerServiceTests
{
    private BrokerService? _brokerService;

    [SetUp]
    public void Setup()
    {
        var registryLogger = Substitute.For<ILogger<ToolRegistry>>();
        var executorLogger = Substitute.For<ILogger<ToolExecutor>>();
        var brokerLogger = Substitute.For<ILogger<BrokerService>>();

        var toolRegistry = new ToolRegistry(registryLogger);
        var toolExecutor = new ToolExecutor(executorLogger);
        _brokerService = new BrokerService(brokerLogger, toolRegistry, toolExecutor);

        _brokerService.Initialize(new[] { typeof(ExecutorTestTools).Assembly });

        ExecutorTestTools.VoidToolWasCalled = false;
    }

    [Test]
    public async Task CallToolAsync_VoidTool_ReturnsSuccess()
    {
        Guard.IsNotNull(_brokerService);

        var result = await _brokerService.CallToolAsync("exec/void", new Dictionary<string, object?>());

        result.IsSuccess.Should().BeTrue();
        ExecutorTestTools.VoidToolWasCalled.Should().BeTrue();
    }

    [Test]
    public async Task CallToolAsync_StringReturnTool_ReturnsValue()
    {
        Guard.IsNotNull(_brokerService);

        var arguments = new Dictionary<string, object?>
        {
            ["name"] = "World"
        };

        var result = await _brokerService.CallToolAsync("exec/greet", arguments);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Hello, World!");
    }

    [Test]
    public async Task CallToolAsync_IntParameters_CoercedCorrectly()
    {
        Guard.IsNotNull(_brokerService);

        var arguments = new Dictionary<string, object?>
        {
            ["left"] = 3,
            ["right"] = 4
        };

        var result = await _brokerService.CallToolAsync("exec/add", arguments);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(7);
    }

    [Test]
    public async Task CallToolAsync_IntParametersAsStrings_CoercedCorrectly()
    {
        Guard.IsNotNull(_brokerService);

        var arguments = new Dictionary<string, object?>
        {
            ["left"] = "10",
            ["right"] = "20"
        };

        var result = await _brokerService.CallToolAsync("exec/add", arguments);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(30);
    }

    [Test]
    public async Task CallToolAsync_IntParametersAsLongs_CoercedCorrectly()
    {
        Guard.IsNotNull(_brokerService);

        // JSON deserializers often produce long for integer values
        var arguments = new Dictionary<string, object?>
        {
            ["left"] = 5L,
            ["right"] = 7L
        };

        var result = await _brokerService.CallToolAsync("exec/add", arguments);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(12);
    }

    [Test]
    public async Task CallToolAsync_OptionalParamOmitted_UsesDefault()
    {
        Guard.IsNotNull(_brokerService);

        var arguments = new Dictionary<string, object?>
        {
            ["value"] = "hello"
        };

        var result = await _brokerService.CallToolAsync("exec/optional", arguments);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello!");
    }

    [Test]
    public async Task CallToolAsync_OptionalParamProvided_UsesProvided()
    {
        Guard.IsNotNull(_brokerService);

        var arguments = new Dictionary<string, object?>
        {
            ["value"] = "hello",
            ["suffix"] = "?"
        };

        var result = await _brokerService.CallToolAsync("exec/optional", arguments);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello?");
    }

    [Test]
    public async Task CallToolAsync_AsyncOk_ReturnsSuccess()
    {
        Guard.IsNotNull(_brokerService);

        var result = await _brokerService.CallToolAsync("exec/async_ok", new Dictionary<string, object?>());

        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public async Task CallToolAsync_AsyncFail_ReturnsFailure()
    {
        Guard.IsNotNull(_brokerService);

        var result = await _brokerService.CallToolAsync("exec/async_fail", new Dictionary<string, object?>());

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Something went wrong");
    }

    [Test]
    public async Task CallToolAsync_AsyncValue_ReturnsPayload()
    {
        Guard.IsNotNull(_brokerService);

        var arguments = new Dictionary<string, object?>
        {
            ["text"] = "hello"
        };

        var result = await _brokerService.CallToolAsync("exec/async_value", arguments);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("HELLO");
    }

    [Test]
    public async Task CallToolAsync_UnknownTool_ReturnsFailure()
    {
        Guard.IsNotNull(_brokerService);

        var result = await _brokerService.CallToolAsync("does/not/exist", new Dictionary<string, object?>());

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unknown tool");
    }

    [Test]
    public async Task CallToolAsync_MissingRequiredParam_ReturnsFailure()
    {
        Guard.IsNotNull(_brokerService);

        var result = await _brokerService.CallToolAsync("exec/greet", new Dictionary<string, object?>());

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Missing required parameter");
    }

    [Test]
    public async Task CallToolAsync_ToolThrows_ReturnsFailure()
    {
        Guard.IsNotNull(_brokerService);

        var result = await _brokerService.CallToolAsync("exec/throws", new Dictionary<string, object?>());

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Deliberate test exception");
    }

    [Test]
    public async Task CallToolAsync_ResourceKeyParam_CoercedFromString()
    {
        Guard.IsNotNull(_brokerService);

        var arguments = new Dictionary<string, object?>
        {
            ["key"] = "Project/readme.md"
        };

        var result = await _brokerService.CallToolAsync("exec/resource_key", arguments);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Project/readme.md");
    }

    [Test]
    public async Task CallToolAsync_BoolParamAsString_CoercedCorrectly()
    {
        Guard.IsNotNull(_brokerService);

        var arguments = new Dictionary<string, object?>
        {
            ["enabled"] = "true"
        };

        var result = await _brokerService.CallToolAsync("exec/bool_param", arguments);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("on");
    }

    [Test]
    public async Task CallToolAsync_InvalidTypeConversion_ReturnsFailure()
    {
        Guard.IsNotNull(_brokerService);

        var arguments = new Dictionary<string, object?>
        {
            ["left"] = "not_a_number",
            ["right"] = "3"
        };

        var result = await _brokerService.CallToolAsync("exec/add", arguments);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Cannot parse");
    }
}
