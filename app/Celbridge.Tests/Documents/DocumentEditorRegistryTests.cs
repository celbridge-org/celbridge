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
        factory1.Priority.Returns(EditorPriority.Default);

        var factory2 = Substitute.For<IDocumentEditorFactory>();
        factory2.SupportedExtensions.Returns(new List<string> { ".md" });
        factory2.Priority.Returns(EditorPriority.Default);

        registry.RegisterFactory(factory1);
        registry.RegisterFactory(factory2);

        registry.GetAllFactories().Should().HaveCount(2);
        registry.IsExtensionSupported(".md").Should().BeTrue();
    }

    [Test]
    public void GetFactory_ReturnsDefaultPriorityFactoryOverOption()
    {
        var registry = new DocumentEditorRegistry();
        var fileResource = new ResourceKey("test.md");
        var filePath = "/path/test.md";

        var optionPriority = Substitute.For<IDocumentEditorFactory>();
        optionPriority.SupportedExtensions.Returns(new List<string> { ".md" });
        optionPriority.Priority.Returns(EditorPriority.Option);
        optionPriority.CanHandle(Arg.Any<ResourceKey>(), Arg.Any<string>()).Returns(true);

        var defaultPriority = Substitute.For<IDocumentEditorFactory>();
        defaultPriority.SupportedExtensions.Returns(new List<string> { ".md" });
        defaultPriority.Priority.Returns(EditorPriority.Default);
        defaultPriority.CanHandle(Arg.Any<ResourceKey>(), Arg.Any<string>()).Returns(true);

        registry.RegisterFactory(optionPriority);
        registry.RegisterFactory(defaultPriority);

        var result = registry.GetFactory(fileResource, filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(defaultPriority);
    }

    [Test]
    public void GetFactory_FallsBackToOptionWhenDefaultCannotHandle()
    {
        var registry = new DocumentEditorRegistry();
        var fileResource = new ResourceKey("test.md");
        var filePath = "/path/test.md";

        var defaultButCantHandle = Substitute.For<IDocumentEditorFactory>();
        defaultButCantHandle.SupportedExtensions.Returns(new List<string> { ".md" });
        defaultButCantHandle.Priority.Returns(EditorPriority.Default);
        defaultButCantHandle.CanHandle(Arg.Any<ResourceKey>(), Arg.Any<string>()).Returns(false);

        var optionCanHandle = Substitute.For<IDocumentEditorFactory>();
        optionCanHandle.SupportedExtensions.Returns(new List<string> { ".md" });
        optionCanHandle.Priority.Returns(EditorPriority.Option);
        optionCanHandle.CanHandle(Arg.Any<ResourceKey>(), Arg.Any<string>()).Returns(true);

        registry.RegisterFactory(defaultButCantHandle);
        registry.RegisterFactory(optionCanHandle);

        var result = registry.GetFactory(fileResource, filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(optionCanHandle);
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
