using Celbridge.Packages;

namespace Celbridge.Tests.Documents;

[TestFixture]
public class MultiPartExtensionResolutionTests
{
    [Test]
    public void GetFactory_PrefersMultiPartExtensionOverSingleCelFallback()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var noteCelFactory = CreateMockFactory("note-cel", ".note.cel");
        var celFactory = CreateMockFactory("cel-fallback", ".cel");

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

        var celFactory = CreateMockFactory("cel-fallback", ".cel");
        registry.RegisterFactory(celFactory);

        var fileResource = new ResourceKey("foo.cel");
        var result = registry.GetFactory(fileResource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(celFactory);
    }

    [Test]
    public void GetFactory_MultiPartWinsEvenWhenRegisteredAfterSingleCel()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        // Both extensions present and both can handle the resource. Longest match
        // selects the extension bucket, independently of registration order.
        var celFactory = CreateMockFactory("cel-fallback", ".cel");
        var noteCelFactory = CreateMockFactory("note-cel", ".note.cel");

        registry.RegisterFactory(celFactory);
        registry.RegisterFactory(noteCelFactory);

        var fileResource = new ResourceKey("foo.note.cel");
        var result = registry.GetFactory(fileResource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(noteCelFactory);
    }

    [Test]
    public void GetFactory_FactoryRegisteringMultipleMultiPartExtensionsMatchesBoth()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var multiFactory = CreateMockFactoryWithExtensions("multi-cel", new[] { ".note.cel", ".theme.cel" });
        registry.RegisterFactory(multiFactory);

        var noteResult = registry.GetFactory(new ResourceKey("foo.note.cel"));
        var modResult = registry.GetFactory(new ResourceKey("bar.theme.cel"));

        noteResult.IsSuccess.Should().BeTrue();
        noteResult.Value.Should().Be(multiFactory);
        modResult.IsSuccess.Should().BeTrue();
        modResult.Value.Should().Be(multiFactory);
    }

    [Test]
    public void GetFactory_SameMultiPartExtensionResolvesInRegistrationOrder()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        // Two instances claiming one extension resolve in declaration order, which the
        // registry records as registration order.
        var firstDeclared = CreateMockFactory("first-cel", ".note.cel");
        var secondDeclared = CreateMockFactory("second-cel", ".note.cel");

        registry.RegisterFactory(firstDeclared);
        registry.RegisterFactory(secondDeclared);

        var result = registry.GetFactory(new ResourceKey("foo.note.cel"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(firstDeclared);
    }

    [Test]
    public void GetFactory_DeclaredInstanceBeatsBuiltInOnSameExtension()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        // The built-in registers first, but declared instances rank ahead of every built-in.
        var builtInFactory = CreateMockFactory(BuiltInEditors.CodeEditorId.ToString(), ".note.cel");
        var declaredInstance = CreateMockFactory("my-notes", ".note.cel");

        registry.RegisterFactory(builtInFactory);
        registry.RegisterFactory(declaredInstance);

        var result = registry.GetFactory(new ResourceKey("foo.note.cel"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(declaredInstance);
    }

    [Test]
    public void GetFactory_MatchesByExactFilenameBeforeExtension()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var packageTomlFactory = CreateMockFactoryWithFilenames("package-toml", new[] { "package.toml" });
        var tomlFactory = CreateMockFactory("toml-fallback", ".toml");

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

        var factory = CreateMockFactoryWithFilenames("filename-only", new[] { "package.toml" });

        var result = registry.RegisterFactory(factory);

        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public void RegisterFactory_RejectsFactoryWithNeitherExtensionNorFilename()
    {
        var registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());

        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.EditorId.Returns(new EditorInstanceId("test.empty-both"));
        factory.DisplayName.Returns("Empty");
        factory.SupportedExtensions.Returns(new List<string>());
        factory.SupportedFilenames.Returns(new List<string>());

        var result = registry.RegisterFactory(factory);

        result.IsFailure.Should().BeTrue();
    }

    private static IDocumentEditorFactory CreateMockFactory(
        string editorId,
        string extension,
        bool canHandle = true)
    {
        return CreateMockFactoryWithExtensions(editorId, new[] { extension }, canHandle);
    }

    private static IDocumentEditorFactory CreateMockFactoryWithExtensions(
        string editorId,
        IReadOnlyList<string> extensions,
        bool canHandle = true)
    {
        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.EditorId.Returns(new EditorInstanceId(editorId));
        factory.DisplayName.Returns(editorId);
        factory.SupportedExtensions.Returns(extensions);
        factory.SupportedFilenames.Returns(Array.Empty<string>());
        factory.CanHandleResource(Arg.Any<ResourceKey>()).Returns(canHandle);
        return factory;
    }

    private static IDocumentEditorFactory CreateMockFactoryWithFilenames(
        string editorId,
        IReadOnlyList<string> filenames,
        bool canHandle = true)
    {
        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.EditorId.Returns(new EditorInstanceId(editorId));
        factory.DisplayName.Returns(editorId);
        factory.SupportedExtensions.Returns(Array.Empty<string>());
        factory.SupportedFilenames.Returns(filenames);
        factory.CanHandleResource(Arg.Any<ResourceKey>()).Returns(canHandle);
        return factory;
    }
}
