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
        factory1.Priority.Returns(EditorPriority.Specialized);

        var factory2 = Substitute.For<IDocumentEditorFactory>();
        factory2.SupportedExtensions.Returns(new List<string> { ".md" });
        factory2.Priority.Returns(EditorPriority.Specialized);

        registry.RegisterFactory(factory1);
        registry.RegisterFactory(factory2);

        registry.GetAllFactories().Should().HaveCount(2);
        registry.IsExtensionSupported(".md").Should().BeTrue();
    }

    [Test]
    public void GetFactory_ReturnsSpecializedPriorityFactoryOverGeneral()
    {
        var registry = new DocumentEditorRegistry();
        var fileResource = new ResourceKey("test.md");
        var filePath = "/path/test.md";

        var generalPriority = Substitute.For<IDocumentEditorFactory>();
        generalPriority.SupportedExtensions.Returns(new List<string> { ".md" });
        generalPriority.Priority.Returns(EditorPriority.General);
        generalPriority.CanHandle(Arg.Any<ResourceKey>(), Arg.Any<string>()).Returns(true);

        var specializedPriority = Substitute.For<IDocumentEditorFactory>();
        specializedPriority.SupportedExtensions.Returns(new List<string> { ".md" });
        specializedPriority.Priority.Returns(EditorPriority.Specialized);
        specializedPriority.CanHandle(Arg.Any<ResourceKey>(), Arg.Any<string>()).Returns(true);

        registry.RegisterFactory(generalPriority);
        registry.RegisterFactory(specializedPriority);

        var result = registry.GetFactory(fileResource, filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(specializedPriority);
    }

    [Test]
    public void GetFactory_FallsBackToGeneralWhenSpecializedCannotHandle()
    {
        var registry = new DocumentEditorRegistry();
        var fileResource = new ResourceKey("test.md");
        var filePath = "/path/test.md";

        var specializedButCantHandle = Substitute.For<IDocumentEditorFactory>();
        specializedButCantHandle.SupportedExtensions.Returns(new List<string> { ".md" });
        specializedButCantHandle.Priority.Returns(EditorPriority.Specialized);
        specializedButCantHandle.CanHandle(Arg.Any<ResourceKey>(), Arg.Any<string>()).Returns(false);

        var generalCanHandle = Substitute.For<IDocumentEditorFactory>();
        generalCanHandle.SupportedExtensions.Returns(new List<string> { ".md" });
        generalCanHandle.Priority.Returns(EditorPriority.General);
        generalCanHandle.CanHandle(Arg.Any<ResourceKey>(), Arg.Any<string>()).Returns(true);

        registry.RegisterFactory(specializedButCantHandle);
        registry.RegisterFactory(generalCanHandle);

        var result = registry.GetFactory(fileResource, filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(generalCanHandle);
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
