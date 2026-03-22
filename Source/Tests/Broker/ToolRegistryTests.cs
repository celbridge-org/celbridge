using Celbridge.Broker;
using Celbridge.Broker.Services;

namespace Celbridge.Tests;

// Test tool methods used by ToolRegistryTests

public static class TestTools
{
    [McpTool(Name = "test/greet", Alias = "greet", Description = "Returns a greeting")]
    public static string Greet(
        [McpParam(Description = "Name to greet")]
        string name)
    {
        return $"Hello, {name}!";
    }

    [McpTool(Name = "test/add", Alias = "add", Description = "Adds two numbers")]
    public static int Add(
        [McpParam(Description = "First number")]
        int left,
        [McpParam(Description = "Second number")]
        int right)
    {
        return left + right;
    }

    [McpTool(Name = "test/optional", Alias = "optional", Description = "Tool with optional parameter")]
    public static string Optional(
        [McpParam(Description = "Required value")]
        string value,
        [McpParam(Description = "Optional flag")]
        bool verbose = false)
    {
        return verbose ? $"Verbose: {value}" : value;
    }
}

public static class MoreTestTools
{
    [McpTool(Name = "other/ping", Alias = "ping", Description = "Returns pong")]
    public static string Ping()
    {
        return "pong";
    }

    // This method has no [McpTool] and should not be discovered
    public static string NotATool()
    {
        return "not a tool";
    }
}

[TestFixture]
public class ToolRegistryTests
{
    private ToolRegistry? _registry;

    [SetUp]
    public void Setup()
    {
        var logger = Substitute.For<ILogger<ToolRegistry>>();
        _registry = new ToolRegistry(logger);
        _registry.DiscoverTools(new[] { typeof(TestTools).Assembly });
    }

    [Test]
    public void DiscoverTools_FindsAllAttributedMethods()
    {
        Guard.IsNotNull(_registry);

        var tools = _registry.GetTools();

        tools.Should().Contain(t => t.Name == "test/greet");
        tools.Should().Contain(t => t.Name == "test/add");
        tools.Should().Contain(t => t.Name == "test/optional");
        tools.Should().Contain(t => t.Name == "other/ping");
    }

    [Test]
    public void DiscoverTools_DoesNotFindUndecoratedMethods()
    {
        Guard.IsNotNull(_registry);

        var tools = _registry.GetTools();

        tools.Should().NotContain(t => t.Name == "NotATool");
    }

    [Test]
    public void FindTool_ReturnsDescriptorForKnownTool()
    {
        Guard.IsNotNull(_registry);

        var descriptor = _registry.FindTool("test/greet");

        descriptor.Should().NotBeNull();
        descriptor!.Name.Should().Be("test/greet");
        descriptor.Description.Should().Be("Returns a greeting");
    }

    [Test]
    public void FindTool_ReturnsNullForUnknownTool()
    {
        Guard.IsNotNull(_registry);

        var descriptor = _registry.FindTool("does/not/exist");

        descriptor.Should().BeNull();
    }

    [Test]
    public void ToolDescriptor_HasCorrectParameterCount()
    {
        Guard.IsNotNull(_registry);

        var greet = _registry.FindTool("test/greet");
        greet.Should().NotBeNull();
        greet!.Parameters.Should().HaveCount(1);

        var add = _registry.FindTool("test/add");
        add.Should().NotBeNull();
        add!.Parameters.Should().HaveCount(2);

        var ping = _registry.FindTool("other/ping");
        ping.Should().NotBeNull();
        ping!.Parameters.Should().HaveCount(0);
    }

    [Test]
    public void ParameterDescriptor_CapturesNameAndDescription()
    {
        Guard.IsNotNull(_registry);

        var greet = _registry.FindTool("test/greet");
        Guard.IsNotNull(greet);

        var nameParameter = greet.Parameters[0];
        nameParameter.Name.Should().Be("name");
        nameParameter.Description.Should().Be("Name to greet");
        nameParameter.TypeName.Should().Be("str");
    }

    [Test]
    public void ParameterDescriptor_CapturesDefaultValues()
    {
        Guard.IsNotNull(_registry);

        var optional = _registry.FindTool("test/optional");
        Guard.IsNotNull(optional);

        var valueParameter = optional.Parameters[0];
        valueParameter.HasDefaultValue.Should().BeFalse();

        var verboseParameter = optional.Parameters[1];
        verboseParameter.HasDefaultValue.Should().BeTrue();
        verboseParameter.DefaultValue.Should().Be(false);
    }

    [Test]
    public void ParameterDescriptor_CapturesParameterType()
    {
        Guard.IsNotNull(_registry);

        var add = _registry.FindTool("test/add");
        Guard.IsNotNull(add);

        add.Parameters[0].ParameterType.Should().Be(typeof(int));
        add.Parameters[1].ParameterType.Should().Be(typeof(int));
    }
}
