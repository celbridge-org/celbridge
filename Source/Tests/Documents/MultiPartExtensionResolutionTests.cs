namespace Celbridge.Tests.Documents;

[TestFixture]
public class MultiPartExtensionResolutionTests
{
    [Test]
    public void GetFactory_PrefersMultiPartExtensionOverSingleCelFallback()
    {
        var registry = new DocumentEditorRegistry();

        var projectCelFactory = CreateMockFactory("test.project-cel", ".project.cel", EditorPriority.Specialized);
        var celFactory = CreateMockFactory("test.cel-fallback", ".cel", EditorPriority.General);

        registry.RegisterFactory(projectCelFactory);
        registry.RegisterFactory(celFactory);

        var fileResource = new ResourceKey("foo.project.cel");
        var result = registry.GetFactory(fileResource, "/path/foo.project.cel");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(projectCelFactory);
    }

    [Test]
    public void GetFactory_FallsBackToSingleCelWhenNoMultiPartFactoryRegistered()
    {
        var registry = new DocumentEditorRegistry();

        var celFactory = CreateMockFactory("test.cel-fallback", ".cel", EditorPriority.General);
        registry.RegisterFactory(celFactory);

        var fileResource = new ResourceKey("foo.cel");
        var result = registry.GetFactory(fileResource, "/path/foo.cel");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(celFactory);
    }

    [Test]
    public void GetFactory_MultiPartWinsEvenWhenSingleCelIsAlsoRegistered()
    {
        var registry = new DocumentEditorRegistry();

        // Both extensions present and both can handle the resource. Longest match
        // wins extension selection independently of the priority bands.
        var projectCelFactory = CreateMockFactory("test.project-cel", ".project.cel", EditorPriority.Specialized);
        var celFactory = CreateMockFactory("test.cel-fallback", ".cel", EditorPriority.General);

        registry.RegisterFactory(projectCelFactory);
        registry.RegisterFactory(celFactory);

        var fileResource = new ResourceKey("foo.project.cel");
        var result = registry.GetFactory(fileResource, "/path/foo.project.cel");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(projectCelFactory);
    }

    [Test]
    public void GetFactory_FactoryRegisteringMultipleMultiPartExtensionsMatchesBoth()
    {
        var registry = new DocumentEditorRegistry();

        var multiFactory = CreateMockFactoryWithExtensions("test.multi-cel", new[] { ".project.cel", ".mod.cel" });
        registry.RegisterFactory(multiFactory);

        var projectResult = registry.GetFactory(new ResourceKey("foo.project.cel"), "/path/foo.project.cel");
        var modResult = registry.GetFactory(new ResourceKey("bar.mod.cel"), "/path/bar.mod.cel");

        projectResult.IsSuccess.Should().BeTrue();
        projectResult.Value.Should().Be(multiFactory);
        modResult.IsSuccess.Should().BeTrue();
        modResult.Value.Should().Be(multiFactory);
    }

    [Test]
    public void GetFactory_SpecializedStillBeatsGeneralOnSameMultiPartExtension()
    {
        var registry = new DocumentEditorRegistry();

        var specialized = CreateMockFactory("test.special-cel", ".project.cel", EditorPriority.Specialized);
        var general = CreateMockFactory("test.general-cel", ".project.cel", EditorPriority.General);

        registry.RegisterFactory(general);
        registry.RegisterFactory(specialized);

        var result = registry.GetFactory(new ResourceKey("foo.project.cel"), "/path/foo.project.cel");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(specialized);
    }

    [Test]
    public void GetFactory_MatchesByExactFilenameBeforeExtension()
    {
        var registry = new DocumentEditorRegistry();

        var packageTomlFactory = CreateMockFactoryWithFilenames("test.package-toml", new[] { "package.toml" });
        var tomlFactory = CreateMockFactory("test.toml-fallback", ".toml", EditorPriority.General);

        registry.RegisterFactory(packageTomlFactory);
        registry.RegisterFactory(tomlFactory);

        var packageResult = registry.GetFactory(new ResourceKey("package.toml"), "/path/package.toml");
        var otherTomlResult = registry.GetFactory(new ResourceKey("other.toml"), "/path/other.toml");

        packageResult.IsSuccess.Should().BeTrue();
        packageResult.Value.Should().Be(packageTomlFactory);

        otherTomlResult.IsSuccess.Should().BeTrue();
        otherTomlResult.Value.Should().Be(tomlFactory);
    }

    [Test]
    public void RegisterFactory_AllowsFilenameOnlyFactory()
    {
        var registry = new DocumentEditorRegistry();

        var factory = CreateMockFactoryWithFilenames("test.filename-only", new[] { "package.toml" });

        var result = registry.RegisterFactory(factory);

        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public void RegisterFactory_RejectsFactoryWithNeitherExtensionNorFilename()
    {
        var registry = new DocumentEditorRegistry();

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
        factory.CanHandleResource(Arg.Any<ResourceKey>(), Arg.Any<string>()).Returns(canHandle);
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
        factory.CanHandleResource(Arg.Any<ResourceKey>(), Arg.Any<string>()).Returns(canHandle);
        return factory;
    }
}
