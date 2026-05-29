namespace Celbridge.Tests.Documents;

[TestFixture]
public class MultiPartExtensionResolutionTests
{
    [Test]
    public void GetFactory_PrefersMultiPartExtensionOverSingleCelFallback()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var noteCelFactory = CreateMockFactory("test.note-cel", ".note.cel", EditorPriority.Specialized);
        var celFactory = CreateMockFactory("test.cel-fallback", ".cel", EditorPriority.General);

        registry.RegisterFactory(noteCelFactory);
        registry.RegisterFactory(celFactory);

        var fileResource = new ResourceKey("foo.note.cel");
        var result = registry.GetFactory(fileResource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(noteCelFactory);
    }

    [Test]
    public void GetFactory_FallsBackToSingleCelWhenNoMultiPartFactoryRegistered()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var celFactory = CreateMockFactory("test.cel-fallback", ".cel", EditorPriority.General);
        registry.RegisterFactory(celFactory);

        var fileResource = new ResourceKey("foo.cel");
        var result = registry.GetFactory(fileResource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(celFactory);
    }

    [Test]
    public void GetFactory_MultiPartWinsEvenWhenSingleCelIsAlsoRegistered()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        // Both extensions present and both can handle the resource. Longest match
        // wins extension selection independently of the priority bands.
        var noteCelFactory = CreateMockFactory("test.note-cel", ".note.cel", EditorPriority.Specialized);
        var celFactory = CreateMockFactory("test.cel-fallback", ".cel", EditorPriority.General);

        registry.RegisterFactory(noteCelFactory);
        registry.RegisterFactory(celFactory);

        var fileResource = new ResourceKey("foo.note.cel");
        var result = registry.GetFactory(fileResource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(noteCelFactory);
    }

    [Test]
    public void GetFactory_FactoryRegisteringMultipleMultiPartExtensionsMatchesBoth()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var multiFactory = CreateMockFactoryWithExtensions("test.multi-cel", new[] { ".note.cel", ".theme.cel" });
        registry.RegisterFactory(multiFactory);

        var noteResult = registry.GetFactory(new ResourceKey("foo.note.cel"));
        var modResult = registry.GetFactory(new ResourceKey("bar.theme.cel"));

        noteResult.IsSuccess.Should().BeTrue();
        noteResult.Value.Should().Be(multiFactory);
        modResult.IsSuccess.Should().BeTrue();
        modResult.Value.Should().Be(multiFactory);
    }

    [Test]
    public void GetFactory_SpecializedStillBeatsGeneralOnSameMultiPartExtension()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var specialized = CreateMockFactory("test.special-cel", ".note.cel", EditorPriority.Specialized);
        var general = CreateMockFactory("test.general-cel", ".note.cel", EditorPriority.General);

        registry.RegisterFactory(general);
        registry.RegisterFactory(specialized);

        var result = registry.GetFactory(new ResourceKey("foo.note.cel"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(specialized);
    }

    [Test]
    public void GetFactory_MatchesByExactFilenameBeforeExtension()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var packageTomlFactory = CreateMockFactoryWithFilenames("test.package-toml", new[] { "package.toml" });
        var tomlFactory = CreateMockFactory("test.toml-fallback", ".toml", EditorPriority.General);

        registry.RegisterFactory(packageTomlFactory);
        registry.RegisterFactory(tomlFactory);

        var packageResult = registry.GetFactory(new ResourceKey("package.toml"));
        var otherTomlResult = registry.GetFactory(new ResourceKey("other.toml"));

        packageResult.IsSuccess.Should().BeTrue();
        packageResult.Value.Should().Be(packageTomlFactory);

        otherTomlResult.IsSuccess.Should().BeTrue();
        otherTomlResult.Value.Should().Be(tomlFactory);
    }

    [Test]
    public void RegisterFactory_AllowsFilenameOnlyFactory()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var factory = CreateMockFactoryWithFilenames("test.filename-only", new[] { "package.toml" });

        var result = registry.RegisterFactory(factory);

        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public void RegisterFactory_RejectsFactoryWithNeitherExtensionNorFilename()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.EditorId.Returns(new DocumentEditorId("test.empty-both"));
        factory.DisplayName.Returns("Empty");
        factory.SupportedExtensions.Returns(new List<string>());
        factory.SupportedFilenames.Returns(new List<string>());

        var result = registry.RegisterFactory(factory);

        result.IsFailure.Should().BeTrue();
    }

    private static IDocumentEditorFactory CreateMockFactory(
        string documentEditorId,
        string extension,
        EditorPriority priority = EditorPriority.Specialized,
        bool canHandle = true)
    {
        return CreateMockFactoryWithExtensions(documentEditorId, new[] { extension }, priority, canHandle);
    }

    private static IDocumentEditorFactory CreateMockFactoryWithExtensions(
        string documentEditorId,
        IReadOnlyList<string> extensions,
        EditorPriority priority = EditorPriority.Specialized,
        bool canHandle = true)
    {
        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.EditorId.Returns(new DocumentEditorId(documentEditorId));
        factory.DisplayName.Returns(documentEditorId);
        factory.SupportedExtensions.Returns(extensions);
        factory.SupportedFilenames.Returns(Array.Empty<string>());
        factory.Priority.Returns(priority);
        factory.CanHandleResource(Arg.Any<ResourceKey>()).Returns(canHandle);
        return factory;
    }

    private static IDocumentEditorFactory CreateMockFactoryWithFilenames(
        string documentEditorId,
        IReadOnlyList<string> filenames,
        EditorPriority priority = EditorPriority.Specialized,
        bool canHandle = true)
    {
        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.EditorId.Returns(new DocumentEditorId(documentEditorId));
        factory.DisplayName.Returns(documentEditorId);
        factory.SupportedExtensions.Returns(Array.Empty<string>());
        factory.SupportedFilenames.Returns(filenames);
        factory.Priority.Returns(priority);
        factory.CanHandleResource(Arg.Any<ResourceKey>()).Returns(canHandle);
        return factory;
    }
}
