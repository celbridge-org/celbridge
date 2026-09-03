using Celbridge.Packages;

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
        factory.EditorId.Returns(new EditorId("test.empty"));
        factory.DisplayName.Returns("Empty");
        factory.SupportedExtensions.Returns(new List<string>());

        var result = registry.RegisterFactory(factory);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void RegisterFactory_AllowsMultipleFactoriesForSameExtension()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var factory1 = CreateMockFactory("test.md-editor-1", ".md");
        var factory2 = CreateMockFactory("test.md-editor-2", ".md");

        registry.RegisterFactory(factory1);
        registry.RegisterFactory(factory2);

        registry.GetAllFactories().Should().HaveCount(2);
        registry.IsExtensionSupported(".md").Should().BeTrue();
    }

    [Test]
    public void RegisterFactory_SkipsDuplicateEditorId()
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
    public void GetFactory_ReturnsDeclaredInstanceAheadOfBuiltIn()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());
        var fileResource = new ResourceKey("test.md");

        // The built-in registers first, but a project-declared editor (dot-free id)
        // resolves ahead of the entire built-in band.
        var builtInFactory = CreateMockFactory(BuiltInEditors.MarkdownEditorId.ToString(), ".md");
        var declaredFactory = CreateMockFactory("my-editor", ".md");

        registry.RegisterFactory(builtInFactory);
        registry.RegisterFactory(declaredFactory);

        var result = registry.GetFactory(fileResource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(declaredFactory);
    }

    [Test]
    public void GetFactory_DeclaredInstancesResolveInRegistrationOrder()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());
        var fileResource = new ResourceKey("test.md");

        var firstEditor = CreateMockFactory("first-editor", ".md");
        var secondEditor = CreateMockFactory("second-editor", ".md");

        registry.RegisterFactory(firstEditor);
        registry.RegisterFactory(secondEditor);

        var result = registry.GetFactory(fileResource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(firstEditor);
    }

    [Test]
    public void GetFactory_BuiltInsResolveInHostOrder()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());
        var fileResource = new ResourceKey("test.md");

        // The code editor registers first, but the pinned order places the
        // markdown editor ahead of it.
        var codeFactory = CreateMockFactory(BuiltInEditors.CodeEditorId.ToString(), ".md");
        var markdownFactory = CreateMockFactory(BuiltInEditors.MarkdownEditorId.ToString(), ".md");

        registry.RegisterFactory(codeFactory);
        registry.RegisterFactory(markdownFactory);

        var result = registry.GetFactory(fileResource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(markdownFactory);
    }

    [Test]
    public void GetFactory_FallsBackToNextFactoryWhenFirstInResolutionOrderCannotHandle()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());
        var fileResource = new ResourceKey("test.md");

        var rejectingEditor = CreateMockFactory("first-editor", ".md", canHandle: false);
        var acceptingEditor = CreateMockFactory("second-editor", ".md", canHandle: true);

        registry.RegisterFactory(rejectingEditor);
        registry.RegisterFactory(acceptingEditor);

        var result = registry.GetFactory(fileResource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(acceptingEditor);
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
        factory.EditorId.Returns(new EditorId("test.multi-ext"));
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
    public void GetFactoriesForExtension_ReturnsFactoriesInResolutionOrder()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        // The built-in registers first but the declared editor band ranks ahead of it.
        var builtInFactory = CreateMockFactory(BuiltInEditors.MarkdownEditorId.ToString(), ".md");
        var declaredFactory = CreateMockFactory("my-editor", ".md");

        registry.RegisterFactory(builtInFactory);
        registry.RegisterFactory(declaredFactory);

        var factories = registry.GetFactoriesForExtension(".md");

        factories.Should().HaveCount(2);
        factories[0].Should().Be(declaredFactory);
        factories[1].Should().Be(builtInFactory);
    }

    [Test]
    public void GetFactoriesForExtension_PlaceholdersRankAheadOfOtherFactories()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var declaredFactory = CreateMockFactory("my-editor", ".widget");

        // A placeholder reserves by definition, which is what DocumentEditorFactoryBase says and what
        // ranks it ahead of the declared editor.
        var placeholderFactory = CreateMockFactory("celbridge.widget-placeholder", ".widget");
        placeholderFactory.IsPlaceholder.Returns(true);
        placeholderFactory.ReservesFileType.Returns(true);

        registry.RegisterFactory(declaredFactory);
        registry.RegisterFactory(placeholderFactory);

        var factories = registry.GetFactoriesForExtension(".widget");

        factories.Should().HaveCount(2);
        factories[0].Should().Be(placeholderFactory);
        factories[1].Should().Be(declaredFactory);
    }

    [Test]
    public void GetFactoriesForExtension_AReservingEditorRanksAheadOfADeclaredEditor()
    {
        // A reserving editor that opens its own file type, rather than reserving the name and opening
        // nothing, still has to win it: a project package claiming the extension must not take it.
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var declaredFactory = CreateMockFactory("acme.config-editor", ".widget");
        var reservingFactory = CreateMockFactory("celbridge.widget-settings", ".widget");
        reservingFactory.ReservesFileType.Returns(true);

        registry.RegisterFactory(declaredFactory);
        registry.RegisterFactory(reservingFactory);

        var factories = registry.GetFactoriesForExtension(".widget");

        factories[0].Should().Be(reservingFactory);
    }

    [Test]
    public void IsReservedResource_AReservedFileType_IsReservedWhicheverFileItIs()
    {
        // Reservation follows the file type, not the file: the factory that opens only one of them
        // still reserves the type for all of them.
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var reservingFactory = CreateMockFactory("celbridge.widget-settings", ".widget", canHandle: false);
        reservingFactory.ReservesFileType.Returns(true);
        registry.RegisterFactory(reservingFactory);

        registry.IsReservedResource(new ResourceKey("other.widget")).Should().BeTrue();
    }

    [Test]
    public void IsReservedResource_AnOrdinaryFileType_IsNotReserved()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        registry.RegisterFactory(CreateMockFactory("acme.notes", ".widget"));

        registry.IsReservedResource(new ResourceKey("notes.widget")).Should().BeFalse();
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

        var result = registry.GetFactoryById(new EditorId("test.my-editor"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(factory);
    }

    [Test]
    public void GetFactoryById_FailsForUnknownId()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var result = registry.GetFactoryById(new EditorId("nonexistent.editor"));

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void GetAssociatedEditorFactory_LongestMatchingSuffixWins()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var noteFactory = CreateMockFactory("note-editor", ".note.cel");
        var celFactory = CreateMockFactory("cel-editor", ".cel");
        registry.RegisterFactory(noteFactory);
        registry.RegisterFactory(celFactory);

        registry.SetEditorAssociations(new Dictionary<string, string>
        {
            [".note.cel"] = "note-editor",
            [".cel"] = "cel-editor",
        });

        var result = registry.GetAssociatedEditorFactory(new ResourceKey("design.note.cel"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(noteFactory);
    }

    [Test]
    public void GetAssociatedEditorFactory_FailsWhenNoEntryMatches()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var factory = CreateMockFactory("my-editor", ".md");
        registry.RegisterFactory(factory);

        registry.SetEditorAssociations(new Dictionary<string, string>
        {
            [".md"] = "my-editor",
        });

        var result = registry.GetAssociatedEditorFactory(new ResourceKey("notes.txt"));

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void GetAssociatedEditorFactory_FailsWhenNamedEditorIsUnregistered()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        registry.SetEditorAssociations(new Dictionary<string, string>
        {
            [".md"] = "ghost-editor",
        });

        var result = registry.GetAssociatedEditorFactory(new ResourceKey("notes.md"));

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void GetAssociatedEditorFactory_FailsWhenEditorCannotHandleResource()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var rejectingFactory = CreateMockFactory("my-editor", ".md", canHandle: false);
        registry.RegisterFactory(rejectingFactory);

        registry.SetEditorAssociations(new Dictionary<string, string>
        {
            [".md"] = "my-editor",
        });

        var result = registry.GetAssociatedEditorFactory(new ResourceKey("notes.md"));

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

    [Test]
    public void GetUserPickableFactoriesForResource_OmitsCodeEditorForAnEditorThatHandlesBinaryContent()
    {
        // The claiming editor declares that it opens the format as binary, which settles the question
        // without the host's extension list having to know the format at all.
        var sniffer = Substitute.For<ITextBinarySniffer>();
        sniffer.IsBinaryExtension(".acme").Returns(false);
        var registry = new DocumentEditorRegistry(sniffer);

        var binaryEditor = CreateMockFactory("acme.model", ".acme", handlesBinaryContent: true);
        var codeEditor = CreateMockFactory(DocumentConstants.CodeEditorId.ToString(), ".cs");

        registry.RegisterFactory(binaryEditor);
        registry.RegisterFactory(codeEditor);

        var candidates = registry.GetUserPickableFactoriesForResource(new ResourceKey("model.acme"));

        candidates.Should().ContainSingle().Which.Should().Be(binaryEditor);
    }

    [Test]
    public void GetUserPickableFactoriesForResource_AppendsCodeEditorForAnEditorThatHandlesText()
    {
        var sniffer = Substitute.For<ITextBinarySniffer>();
        sniffer.IsBinaryExtension(".note").Returns(false);
        var registry = new DocumentEditorRegistry(sniffer);

        var noteEditor = CreateMockFactory("acme.note", ".note");
        var codeEditor = CreateMockFactory(DocumentConstants.CodeEditorId.ToString(), ".cs");

        registry.RegisterFactory(noteEditor);
        registry.RegisterFactory(codeEditor);

        var candidates = registry.GetUserPickableFactoriesForResource(new ResourceKey("todo.note"));

        candidates.Should().Equal(noteEditor, codeEditor);
    }

    private static IDocumentEditorFactory CreateMockFactory(
        string editorId,
        string extension,
        bool canHandle = true,
        bool handlesBinaryContent = false)
    {
        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.EditorId.Returns(new EditorId(editorId));
        factory.DisplayName.Returns(editorId);
        factory.SupportedExtensions.Returns(new List<string> { extension });
        factory.CanHandleResource(Arg.Any<ResourceKey>()).Returns(canHandle);
        factory.HandlesBinaryContent.Returns(handlesBinaryContent);
        return factory;
    }
}
