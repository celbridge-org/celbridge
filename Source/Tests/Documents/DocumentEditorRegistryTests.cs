namespace Celbridge.Tests.Documents;

[TestFixture]
public class DocumentEditorRegistryTests
{
    [Test]
    public void RegisterFactory_AddsFactoryToRegistry()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var factory = CreateMockFactory("test.md-editor", ".md");

        var result = registry.RegisterFactory(factory);

        result.IsSuccess.Should().BeTrue();
        registry.IsExtensionSupported(".md").Should().BeTrue();
    }

    [Test]
    public void RegisterFactory_FailsWithEmptySupportedExtensions()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.EditorId.Returns(new EditorInstanceId("test.empty"));
        factory.DisplayName.Returns("Empty");
        factory.SupportedExtensions.Returns(new List<string>());

        var result = registry.RegisterFactory(factory);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void RegisterFactory_AllowsMultipleFactoriesForSameExtension()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var factory1 = CreateMockFactory("test.md-editor-1", ".md", EditorPriority.Specialized);
        var factory2 = CreateMockFactory("test.md-editor-2", ".md", EditorPriority.Specialized);

        registry.RegisterFactory(factory1);
        registry.RegisterFactory(factory2);

        registry.GetAllFactories().Should().HaveCount(2);
        registry.IsExtensionSupported(".md").Should().BeTrue();
    }

    [Test]
    public void RegisterFactory_SkipsDuplicateEditorInstanceId()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

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
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());
        var fileResource = new ResourceKey("test.md");

        var generalPriority = CreateMockFactory("test.general", ".md", EditorPriority.General, canHandle: true);
        var specializedPriority = CreateMockFactory("test.specialized", ".md", EditorPriority.Specialized, canHandle: true);

        registry.RegisterFactory(generalPriority);
        registry.RegisterFactory(specializedPriority);

        var result = registry.GetFactory(fileResource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(specializedPriority);
    }

    [Test]
    public void GetFactory_FallsBackToGeneralWhenSpecializedCannotHandle()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());
        var fileResource = new ResourceKey("test.md");

        var specializedButCantHandle = CreateMockFactory("test.specialized", ".md", EditorPriority.Specialized, canHandle: false);
        var generalCanHandleResource = CreateMockFactory("test.general", ".md", EditorPriority.General, canHandle: true);

        registry.RegisterFactory(specializedButCantHandle);
        registry.RegisterFactory(generalCanHandleResource);

        var result = registry.GetFactory(fileResource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(generalCanHandleResource);
    }

    [Test]
    public void GetFactory_FailsWhenNoFactoryCanHandleResource()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());
        var fileResource = new ResourceKey("test.md");

        var factory = CreateMockFactory("test.md-editor", ".md", canHandle: false);

        registry.RegisterFactory(factory);

        var result = registry.GetFactory(fileResource);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void GetFactory_FailsForUnregisteredExtension()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());
        var fileResource = new ResourceKey("test.xyz");

        var result = registry.GetFactory(fileResource);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void IsExtensionSupported_ReturnsFalseForUnregisteredExtension()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        registry.IsExtensionSupported(".xyz").Should().BeFalse();
    }

    [Test]
    public void IsExtensionSupported_IsCaseInsensitive()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var factory = CreateMockFactory("test.upper", ".MD");

        registry.RegisterFactory(factory);

        registry.IsExtensionSupported(".md").Should().BeTrue();
        registry.IsExtensionSupported(".MD").Should().BeTrue();
        registry.IsExtensionSupported(".Md").Should().BeTrue();
    }

    [Test]
    public void GetFactory_HandlesMultipleExtensionsPerFactory()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.EditorId.Returns(new EditorInstanceId("test.multi-ext"));
        factory.DisplayName.Returns("Multi Extension Editor");
        factory.SupportedExtensions.Returns(new List<string> { ".md", ".markdown", ".mdown" });
        factory.CanHandleResource(Arg.Any<ResourceKey>()).Returns(true);

        registry.RegisterFactory(factory);

        registry.IsExtensionSupported(".md").Should().BeTrue();
        registry.IsExtensionSupported(".markdown").Should().BeTrue();
        registry.IsExtensionSupported(".mdown").Should().BeTrue();

        var result1 = registry.GetFactory(new ResourceKey("test.md"));
        var result2 = registry.GetFactory(new ResourceKey("test.markdown"));

        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        result1.Value.Should().Be(factory);
        result2.Value.Should().Be(factory);
    }

    [Test]
    public void GetAllFactories_ReturnsAllRegisteredFactories()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

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
    public void GetFactoriesForExtension_ReturnsAllFactoriesSortedByPriority()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var generalFactory = CreateMockFactory("test.general", ".md", EditorPriority.General);
        var specializedFactory = CreateMockFactory("test.specialized", ".md", EditorPriority.Specialized);

        registry.RegisterFactory(generalFactory);
        registry.RegisterFactory(specializedFactory);

        var factories = registry.GetFactoriesForExtension(".md");

        factories.Should().HaveCount(2);
        factories[0].Should().Be(specializedFactory);
        factories[1].Should().Be(generalFactory);
    }

    [Test]
    public void GetFactoriesForExtension_ReturnsEmptyForUnknownExtension()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var factories = registry.GetFactoriesForExtension(".xyz");

        factories.Should().BeEmpty();
    }

    [Test]
    public void GetFactoryById_ReturnsCorrectFactory()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var factory = CreateMockFactory("test.my-editor", ".md");
        registry.RegisterFactory(factory);

        var result = registry.GetFactoryById(new EditorInstanceId("test.my-editor"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(factory);
    }

    [Test]
    public void GetFactoryById_FailsForUnknownId()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var result = registry.GetFactoryById(new EditorInstanceId("nonexistent.editor"));

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void GetUserPickableFactoriesForResource_FiltersPlaceholders()
    {
        var sniffer = Substitute.For<ITextBinarySniffer>();
        sniffer.IsBinaryExtension(Arg.Any<string>()).Returns(true);
        var registry = new DocumentEditorRegistry(sniffer);

        var placeholder = CreateMockFactory("acme.placeholder", ".widget");
        placeholder.IsPlaceholder.Returns(true);
        var real = CreateMockFactory("acme.real", ".widget");

        registry.RegisterFactory(placeholder);
        registry.RegisterFactory(real);

        var candidates = registry.GetUserPickableFactoriesForResource(new ResourceKey("design.widget"));

        candidates.Should().ContainSingle().Which.Should().Be(real);
    }

    [Test]
    public void GetUserPickableFactoriesForResource_AppendsCodeEditorForTextShapedFiles()
    {
        var sniffer = Substitute.For<ITextBinarySniffer>();
        sniffer.IsBinaryExtension(".md").Returns(false);
        var registry = new DocumentEditorRegistry(sniffer);

        var specialized = CreateMockFactory("acme.markdown", ".md");
        var codeEditor = CreateMockFactory(DocumentConstants.CodeEditorId.ToString(), ".cs");

        registry.RegisterFactory(specialized);
        registry.RegisterFactory(codeEditor);

        var candidates = registry.GetUserPickableFactoriesForResource(new ResourceKey("readme.md"));

        candidates.Should().HaveCount(2);
        candidates.Should().Contain(specialized);
        candidates.Should().Contain(codeEditor);
    }

    [Test]
    public void GetUserPickableFactoriesForResource_OmitsCodeEditorForBinaryFiles()
    {
        var sniffer = Substitute.For<ITextBinarySniffer>();
        sniffer.IsBinaryExtension(".png").Returns(true);
        var registry = new DocumentEditorRegistry(sniffer);

        var imageEditor = CreateMockFactory("acme.image", ".png");
        var codeEditor = CreateMockFactory(DocumentConstants.CodeEditorId.ToString(), ".cs");

        registry.RegisterFactory(imageEditor);
        registry.RegisterFactory(codeEditor);

        var candidates = registry.GetUserPickableFactoriesForResource(new ResourceKey("photo.png"));

        candidates.Should().ContainSingle().Which.Should().Be(imageEditor);
    }

    [Test]
    public void GetUserPickableFactoriesForResource_DoesNotDuplicateCodeEditorWhenAlreadyClaimingExtension()
    {
        var sniffer = Substitute.For<ITextBinarySniffer>();
        sniffer.IsBinaryExtension(".cs").Returns(false);
        var registry = new DocumentEditorRegistry(sniffer);

        var codeEditor = CreateMockFactory(DocumentConstants.CodeEditorId.ToString(), ".cs");
        registry.RegisterFactory(codeEditor);

        var candidates = registry.GetUserPickableFactoriesForResource(new ResourceKey("program.cs"));

        candidates.Should().ContainSingle().Which.Should().Be(codeEditor);
    }

    private static IDocumentEditorFactory CreateMockFactory(
        string editorId,
        string extension,
        EditorPriority priority = EditorPriority.Specialized,
        bool canHandle = true)
    {
        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.EditorId.Returns(new EditorInstanceId(editorId));
        factory.DisplayName.Returns(editorId);
        factory.SupportedExtensions.Returns(new List<string> { extension });
        factory.Priority.Returns(priority);
        factory.CanHandleResource(Arg.Any<ResourceKey>()).Returns(canHandle);
        return factory;
    }
}
