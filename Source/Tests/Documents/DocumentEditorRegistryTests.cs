namespace Celbridge.Tests.Documents;

[TestFixture]
public class DocumentEditorRegistryTests
{
    [Test]
    public void RegisterFactory_AddsFactoryToRegistry()
    {
        var registry = new DocumentEditorRegistry();

        var factory = CreateMockFactory("test.md-editor", ".md");

        var result = registry.RegisterFactory(factory);

        result.IsSuccess.Should().BeTrue();
        registry.IsExtensionSupported(".md").Should().BeTrue();
    }

    [Test]
    public void RegisterFactory_FailsWithEmptySupportedExtensions()
    {
        var registry = new DocumentEditorRegistry();

        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.EditorId.Returns(new DocumentEditorId("test.empty"));
        factory.DisplayName.Returns("Empty");
        factory.SupportedExtensions.Returns(new List<string>());

        var result = registry.RegisterFactory(factory);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void RegisterFactory_AllowsMultipleFactoriesForSameExtension()
    {
        var registry = new DocumentEditorRegistry();

        var factory1 = CreateMockFactory("test.md-editor-1", ".md", EditorPriority.Specialized);
        var factory2 = CreateMockFactory("test.md-editor-2", ".md", EditorPriority.Specialized);

        registry.RegisterFactory(factory1);
        registry.RegisterFactory(factory2);

        registry.GetAllFactories().Should().HaveCount(2);
        registry.IsExtensionSupported(".md").Should().BeTrue();
    }

    [Test]
    public void RegisterFactory_SkipsDuplicateDocumentEditorId()
    {
        var registry = new DocumentEditorRegistry();

        var factory1 = CreateMockFactory("test.duplicate", ".md");
        var factory2 = CreateMockFactory("test.duplicate", ".txt");

        var result1 = registry.RegisterFactory(factory1);
        var result2 = registry.RegisterFactory(factory2);

        result1.IsSuccess.Should().BeTrue();
        result2.IsFailure.Should().BeTrue();
        registry.GetAllFactories().Should().HaveCount(1);
    }

    [Test]
    public void GetFactory_ReturnsSpecializedPriorityFactoryOverGeneral()
    {
        var registry = new DocumentEditorRegistry();
        var fileResource = new ResourceKey("test.md");
        var filePath = "/path/test.md";

        var generalPriority = CreateMockFactory("test.general", ".md", EditorPriority.General, canHandle: true);
        var specializedPriority = CreateMockFactory("test.specialized", ".md", EditorPriority.Specialized, canHandle: true);

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

        var specializedButCantHandle = CreateMockFactory("test.specialized", ".md", EditorPriority.Specialized, canHandle: false);
        var generalCanHandleResource = CreateMockFactory("test.general", ".md", EditorPriority.General, canHandle: true);

        registry.RegisterFactory(specializedButCantHandle);
        registry.RegisterFactory(generalCanHandleResource);

        var result = registry.GetFactory(fileResource, filePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(generalCanHandleResource);
    }

    [Test]
    public void GetFactory_FailsWhenNoFactoryCanHandleResource()
    {
        var registry = new DocumentEditorRegistry();
        var fileResource = new ResourceKey("test.md");
        var filePath = "/path/test.md";

        var factory = CreateMockFactory("test.md-editor", ".md", canHandle: false);

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

        var factory = CreateMockFactory("test.upper", ".MD");

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
        factory.EditorId.Returns(new DocumentEditorId("test.multi-ext"));
        factory.DisplayName.Returns("Multi Extension Editor");
        factory.SupportedExtensions.Returns(new List<string> { ".md", ".markdown", ".mdown" });
        factory.CanHandleResource(Arg.Any<ResourceKey>(), Arg.Any<string>()).Returns(true);

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

        var factory1 = CreateMockFactory("test.md-editor", ".md");
        var factory2 = CreateMockFactory("test.txt-editor", ".txt");

        registry.RegisterFactory(factory1);
        registry.RegisterFactory(factory2);

        var allFactories = registry.GetAllFactories();

        allFactories.Should().HaveCount(2);
        allFactories.Should().Contain(factory1);
        allFactories.Should().Contain(factory2);
    }

    [Test]
    public void GetFactoriesForFileExtension_ReturnsAllFactoriesSortedByPriority()
    {
        var registry = new DocumentEditorRegistry();

        var generalFactory = CreateMockFactory("test.general", ".md", EditorPriority.General);
        var specializedFactory = CreateMockFactory("test.specialized", ".md", EditorPriority.Specialized);

        registry.RegisterFactory(generalFactory);
        registry.RegisterFactory(specializedFactory);

        var factories = registry.GetFactoriesForFileExtension(".md");

        factories.Should().HaveCount(2);
        factories[0].Should().Be(specializedFactory);
        factories[1].Should().Be(generalFactory);
    }

    [Test]
    public void GetFactoriesForFileExtension_ReturnsEmptyForUnknownExtension()
    {
        var registry = new DocumentEditorRegistry();

        var factories = registry.GetFactoriesForFileExtension(".xyz");

        factories.Should().BeEmpty();
    }

    [Test]
    public void GetFactoryById_ReturnsCorrectFactory()
    {
        var registry = new DocumentEditorRegistry();

        var factory = CreateMockFactory("test.my-editor", ".md");
        registry.RegisterFactory(factory);

        var result = registry.GetFactoryById("test.my-editor");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(factory);
    }

    [Test]
    public void GetFactoryById_FailsForUnknownId()
    {
        var registry = new DocumentEditorRegistry();

        var result = registry.GetFactoryById("nonexistent.editor");

        result.IsFailure.Should().BeTrue();
    }

    private static IDocumentEditorFactory CreateMockFactory(
        string documentEditorId,
        string extension,
        EditorPriority priority = EditorPriority.Specialized,
        bool canHandle = true)
    {
        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.EditorId.Returns(new DocumentEditorId(documentEditorId));
        factory.DisplayName.Returns(documentEditorId);
        factory.SupportedExtensions.Returns(new List<string> { extension });
        factory.Priority.Returns(priority);
        factory.CanHandleResource(Arg.Any<ResourceKey>(), Arg.Any<string>()).Returns(canHandle);
        return factory;
    }
}
