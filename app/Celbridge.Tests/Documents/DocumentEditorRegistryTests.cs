namespace Celbridge.Tests.Documents;

[TestFixture]
public class DocumentEditorRegistryTests
{
    [Test]
    public void RegisterFactory_AddsFactoryToRegistry()
    {
        var registry = new DocumentEditorRegistry();

        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.SupportedExtensions.Returns(new List<string> { ".md" });

        var result = registry.RegisterFactory(factory);

        result.IsSuccess.Should().BeTrue();
        registry.IsExtensionSupported(".md").Should().BeTrue();
    }

    [Test]
    public void RegisterFactory_FailsWithEmptySupportedExtensions()
    {
        var registry = new DocumentEditorRegistry();

        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.SupportedExtensions.Returns(new List<string>());

        var result = registry.RegisterFactory(factory);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void RegisterFactory_AllowsMultipleFactoriesForSameExtension()
    {
        var registry = new DocumentEditorRegistry();

        var factory1 = Substitute.For<IDocumentEditorFactory>();
        factory1.SupportedExtensions.Returns(new List<string> { ".md" });
        factory1.Priority.Returns(0);

        var factory2 = Substitute.For<IDocumentEditorFactory>();
        factory2.SupportedExtensions.Returns(new List<string> { ".md" });
        factory2.Priority.Returns(10);

        registry.RegisterFactory(factory1);
        registry.RegisterFactory(factory2);

        registry.GetAllFactories().Should().HaveCount(2);
        registry.IsExtensionSupported(".md").Should().BeTrue();
    }

    [Test]
    public void GetFactory_ReturnsHighestPriorityFactory()
    {
        var registry = new DocumentEditorRegistry();
        var fileResource = new ResourceKey("test.md");
        var filePath = "/path/test.md";

        var lowPriority = Substitute.For<IDocumentEditorFactory>();
        lowPriority.SupportedExtensions.Returns(new List<string> { ".md" });
        lowPriority.Priority.Returns(0);
        lowPriority.CanHandle(Arg.Any<ResourceKey>(), Arg.Any<string>()).Returns(true);

        var highPriority = Substitute.For<IDocumentEditorFactory>();
        highPriority.SupportedExtensions.Returns(new List<string> { ".md" });
        highPriority.Priority.Returns(10);
        highPriority.CanHandle(Arg.Any<ResourceKey>(), Arg.Any<string>()).Returns(true);

        registry.RegisterFactory(lowPriority);
        registry.RegisterFactory(highPriority);

        var result = registry.GetFactory(fileResource, filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(highPriority);
    }

    [Test]
    public void GetFactory_SkipsFactoryThatCannotHandleResource()
    {
        var registry = new DocumentEditorRegistry();
        var fileResource = new ResourceKey("test.md");
        var filePath = "/path/test.md";

        var highPriorityButCantHandle = Substitute.For<IDocumentEditorFactory>();
        highPriorityButCantHandle.SupportedExtensions.Returns(new List<string> { ".md" });
        highPriorityButCantHandle.Priority.Returns(10);
        highPriorityButCantHandle.CanHandle(Arg.Any<ResourceKey>(), Arg.Any<string>()).Returns(false);

        var lowPriorityCanHandle = Substitute.For<IDocumentEditorFactory>();
        lowPriorityCanHandle.SupportedExtensions.Returns(new List<string> { ".md" });
        lowPriorityCanHandle.Priority.Returns(0);
        lowPriorityCanHandle.CanHandle(Arg.Any<ResourceKey>(), Arg.Any<string>()).Returns(true);

        registry.RegisterFactory(highPriorityButCantHandle);
        registry.RegisterFactory(lowPriorityCanHandle);

        var result = registry.GetFactory(fileResource, filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(lowPriorityCanHandle);
    }

    [Test]
    public void GetFactory_FailsWhenNoFactoryCanHandle()
    {
        var registry = new DocumentEditorRegistry();
        var fileResource = new ResourceKey("test.md");
        var filePath = "/path/test.md";

        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.SupportedExtensions.Returns(new List<string> { ".md" });
        factory.CanHandle(Arg.Any<ResourceKey>(), Arg.Any<string>()).Returns(false);

        registry.RegisterFactory(factory);

        var result = registry.GetFactory(fileResource, filePath);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void GetFactory_FailsForUnregisteredExtension()
    {
        var registry = new DocumentEditorRegistry();
        var fileResource = new ResourceKey("test.xyz");
        var filePath = "/path/test.xyz";

        var result = registry.GetFactory(fileResource, filePath);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void IsExtensionSupported_ReturnsFalseForUnregisteredExtension()
    {
        var registry = new DocumentEditorRegistry();

        registry.IsExtensionSupported(".xyz").Should().BeFalse();
    }

    [Test]
    public void IsExtensionSupported_IsCaseInsensitive()
    {
        var registry = new DocumentEditorRegistry();

        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.SupportedExtensions.Returns(new List<string> { ".MD" });

        registry.RegisterFactory(factory);

        registry.IsExtensionSupported(".md").Should().BeTrue();
        registry.IsExtensionSupported(".MD").Should().BeTrue();
        registry.IsExtensionSupported(".Md").Should().BeTrue();
    }

    [Test]
    public void GetFactory_HandlesMultipleExtensionsPerFactory()
    {
        var registry = new DocumentEditorRegistry();

        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.SupportedExtensions.Returns(new List<string> { ".md", ".markdown", ".mdown" });
        factory.CanHandle(Arg.Any<ResourceKey>(), Arg.Any<string>()).Returns(true);

        registry.RegisterFactory(factory);

        registry.IsExtensionSupported(".md").Should().BeTrue();
        registry.IsExtensionSupported(".markdown").Should().BeTrue();
        registry.IsExtensionSupported(".mdown").Should().BeTrue();

        var result1 = registry.GetFactory(new ResourceKey("test.md"), "/path/test.md");
        var result2 = registry.GetFactory(new ResourceKey("test.markdown"), "/path/test.markdown");

        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        result1.Value.Should().Be(factory);
        result2.Value.Should().Be(factory);
    }

    [Test]
    public void GetAllFactories_ReturnsAllRegisteredFactories()
    {
        var registry = new DocumentEditorRegistry();

        var factory1 = Substitute.For<IDocumentEditorFactory>();
        factory1.SupportedExtensions.Returns(new List<string> { ".md" });

        var factory2 = Substitute.For<IDocumentEditorFactory>();
        factory2.SupportedExtensions.Returns(new List<string> { ".txt" });

        registry.RegisterFactory(factory1);
        registry.RegisterFactory(factory2);

        var allFactories = registry.GetAllFactories();

        allFactories.Should().HaveCount(2);
        allFactories.Should().Contain(factory1);
        allFactories.Should().Contain(factory2);
    }
}
