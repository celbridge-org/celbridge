using Celbridge.Documents.Helpers;
using Celbridge.Packages;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Tests that FileTypeHelper treats a file as viewer-only exactly when the File Viewer registers the file's extension.
/// The File Viewer's manifest is then the one list of viewer-only types.
/// </summary>
[TestFixture]
public class FileTypeHelperTests
{
    private DocumentEditorRegistry _registry = null!;
    private FileTypeHelper _fileTypeHelper = null!;

    [SetUp]
    public void Setup()
    {
        _registry = new DocumentEditorRegistry(Substitute.For<ITextBinarySniffer>());
        _registry.RegisterFactory(CreateFactory(BuiltInEditors.FileViewerId, ".avif"));

        _fileTypeHelper = new FileTypeHelper();
        _fileTypeHelper.SetDocumentEditorRegistry(_registry);
    }

    [TestCase("photo.avif")]
    [TestCase("PHOTO.AVIF")]
    public void GetDocumentViewType_AnExtensionTheFileViewerRegisters_IsTheFileViewer(string fileName)
    {
        _fileTypeHelper.GetDocumentViewType(fileName).Should().Be(DocumentViewType.FileViewer);
    }

    [Test]
    public void GetDocumentViewType_AnExtensionTheFileViewerDoesNotRegister_IsNotTheFileViewer()
    {
        // A viewer-only file opens only in the File Viewer, so the File Viewer must register every viewer-only type.
        _fileTypeHelper.GetDocumentViewType("font.woff").Should().NotBe(DocumentViewType.FileViewer);
    }

    [Test]
    public void GetDocumentViewType_AnotherEditorsExtension_IsNotTheFileViewer()
    {
        _registry.RegisterFactory(CreateFactory(new EditorId("test.image-editor"), ".png"));

        _fileTypeHelper.GetDocumentViewType("photo.png").Should().NotBe(DocumentViewType.FileViewer);
    }

    private static IDocumentEditorFactory CreateFactory(EditorId editorId, string extension)
    {
        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.EditorId.Returns(editorId);
        factory.DisplayName.Returns(editorId.ToString());
        factory.SupportedExtensions.Returns(new List<string> { extension });
        factory.CanHandleResource(Arg.Any<ResourceKey>()).Returns(true);

        return factory;
    }
}
