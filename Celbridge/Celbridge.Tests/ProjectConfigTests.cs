using Celbridge.Projects;
using Celbridge.Projects.Services;

namespace Celbridge.Tests;

[TestFixture]
public class ProjectConfigTests
{
    private IProjectConfig? _projectConfig;

    [SetUp]
    public void Setup()
    {
        _projectConfig = new ProjectConfig();
    }

    [TearDown]
    public void TearDown()
    {
        _projectConfig = null;
    }

    [Test]
    public void ICanGetProperties()
    {
        var config = new ProjectConfig();
        var jsonContent = "{\"setting1\": \"value1\", \"setting2\": 42}";

        var result = config.Initialize(jsonContent);
        result.IsSuccess.Should().BeTrue();

        string setting1 = config.GetProperty("setting1");
        setting1.Should().Be("value1");

        string setting2 = config.GetProperty("setting2");
        setting2.Should().Be("42");
    }

    [Test]
    public void ICanGetADefaultProperty()
    {
        var config = new ProjectConfig();

        string setting1 = config.GetProperty("setting1", "default");
        setting1.Should().Be("default");
    }

    [Test]
    public void ICanSetAProperty()
    {
        var config = new ProjectConfig();

        config.HasProperty("setting1").Should().BeFalse();

        config.SetProperty("setting1", "new value");

        config.HasProperty("setting1").Should().BeTrue();

        string setting1 = config.GetProperty("setting1", "default");
        setting1.Should().Be("new value");
    }
}
