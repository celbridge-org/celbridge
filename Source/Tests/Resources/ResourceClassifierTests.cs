using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Direct unit tests for the resource classification pass: parent pairing,
/// parentless classification (standalone-form vs orphan), per-file FileKind
/// stamping, and the broken / healthy split. The previous behaviour was tested
/// only end-to-end through ResourceRegistry with a nullable workspace wrapper,
/// which silently disabled the standalone-form recognition in tests; this
/// fixture covers the cross-domain decision directly.
///
/// The classifier reads sidecar bytes from disk to drive SidecarHelper.Inspect,
/// so tests still set up real files; the value is that they target the service
/// surface rather than the registry.
/// </summary>
[TestFixture]
public class ResourceClassifierTests
{
    private string _projectFolderPath = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ResourceClassifierTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolderPath))
        {
            try
            {
                Directory.Delete(_projectFolderPath, true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    [Test]
    public void StandaloneCelWithMultiPartExtensionRegistration_IsNotReportedAsOrphan()
    {
        // A .cel file whose multi-part extension is claimed by a registered
        // editor factory is a standalone .cel form (e.g. foo.webview.cel,
        // foo.note.cel). It has no parent and must not appear in Orphan.
        File.WriteAllText(Path.Combine(_projectFolderPath, "feature.note.cel"),
            "[note]\ntitle = \"Hello\"\n");

        var editorRegistry = Substitute.For<IDocumentEditorRegistry>();
        editorRegistry.IsExtensionSupported(".note.cel").Returns(true);

        var classifier = ResourceClassifierTestHelper.BuildClassifier(editorRegistry);
        var registry = BuildRegistry(classifier);
        registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        var report = registry.GetSidecarReport();
        report.Orphan.Should().NotContain(new ResourceKey("feature.note.cel"));
        report.Healthy.Should().Contain(new ResourceKey("feature.note.cel"));
    }

    [Test]
    public void BareCelExtensionRegistration_DoesNotPreventOrphanReport()
    {
        // The ".cel" extension is also registered as a generic code-editor
        // language (for syntax highlighting), and that registration must not
        // be treated as evidence of a standalone .cel form. A parentless
        // ".cel" whose only matching registration is the bare extension is a
        // true orphan and must appear in the report.
        File.WriteAllText(Path.Combine(_projectFolderPath, "orphaned.png.cel"),
            "tags = [\"orphan\"]\n");

        var editorRegistry = Substitute.For<IDocumentEditorRegistry>();
        editorRegistry.IsExtensionSupported(".cel").Returns(true);

        var classifier = ResourceClassifierTestHelper.BuildClassifier(editorRegistry);
        var registry = BuildRegistry(classifier);
        registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        var report = registry.GetSidecarReport();
        report.Orphan.Should().Contain(new ResourceKey("orphaned.png.cel"));
    }

    [Test]
    public void OrphanCelWithNoFactoryClaim_IsStillReportedAsOrphan()
    {
        // When the editor registry is wired up but no factory claims the
        // file, the .cel is a genuine orphan that the user needs to repair.
        // The registry hookup must not paper over real orphans.
        File.WriteAllText(Path.Combine(_projectFolderPath, "scratch.unknown.cel"),
            "key = \"value\"\n");

        var classifier = ResourceClassifierTestHelper.BuildClassifierWithNoFactories();
        var registry = BuildRegistry(classifier);
        registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        var report = registry.GetSidecarReport();
        report.Orphan.Should().Contain(new ResourceKey("scratch.unknown.cel"));
    }

    [Test]
    public void ParentedSidecar_IsNeverConsultedAgainstEditorRegistry()
    {
        // A .cel that pairs with a sibling parent is never a candidate for
        // standalone-form classification, so the editor registry must not
        // be queried for it. Guards against an edge case where a hypothetical
        // factory match would otherwise mis-classify a real sidecar.
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png"), "data");
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png.cel"),
            "tags = [\"x\"]\n");

        var editorRegistry = Substitute.For<IDocumentEditorRegistry>();
        editorRegistry.IsExtensionSupported(Arg.Any<string>()).Returns(false);

        var classifier = ResourceClassifierTestHelper.BuildClassifier(editorRegistry);
        var registry = BuildRegistry(classifier);
        registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        editorRegistry.DidNotReceive().IsExtensionSupported(Arg.Any<string>());

        var report = registry.GetSidecarReport();
        report.Healthy.Should().Contain(new ResourceKey("foo.png.cel"));
        report.Orphan.Should().NotContain(new ResourceKey("foo.png.cel"));
    }

    [Test]
    public void NestedFolders_PairCorrectly_AndReportUsesRelativeKeys()
    {
        // Make sure the pairing pass walks nested folders and produces project-
        // relative keys (not just leaf names). Catches a regression where the
        // service mistakenly built keys from leaf-only names.
        var sub = Path.Combine(_projectFolderPath, "subfolder");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "note.md"), "body");
        File.WriteAllText(Path.Combine(sub, "note.md.cel"), "tags = [\"meeting\"]\n");

        var classifier = ResourceClassifierTestHelper.BuildClassifierWithNoFactories();
        var registry = BuildRegistry(classifier);
        registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        var noteResource = registry.GetResource(new ResourceKey("subfolder/note.md")).Value as IFileResource;
        noteResource!.Sidecar.Should().NotBeNull();
        noteResource.Sidecar!.Key.Should().Be(new ResourceKey("subfolder/note.md.cel"));
        noteResource.Sidecar.Status.Should().Be(CelFileStatus.Healthy);

        registry.GetSidecarReport()
            .Healthy.Should().Contain(new ResourceKey("subfolder/note.md.cel"));
    }

    [Test]
    public void EmptyTree_ProducesEmptyReport()
    {
        var classifier = ResourceClassifierTestHelper.BuildClassifierWithNoFactories();
        var registry = BuildRegistry(classifier);
        registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        var report = registry.GetSidecarReport();
        report.Healthy.Should().BeEmpty();
        report.Broken.Should().BeEmpty();
        report.Orphan.Should().BeEmpty();
    }

    [Test]
    public void Classify_PlainDataFileWithoutSidecar_IsPlainData()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md"), "# Notes\n");

        var classifier = ResourceClassifierTestHelper.BuildClassifierWithNoFactories();
        var registry = BuildRegistry(classifier);
        registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        var file = registry.GetResource(new ResourceKey("notes.md")).Value as IFileResource;
        file!.FileKind.Should().Be(FileKind.PlainData);
    }

    [Test]
    public void Classify_PairedSidecarAndParent_AssignsExpectedKinds()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md"), "# Notes\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md.cel"), "tags = []\n");

        var classifier = ResourceClassifierTestHelper.BuildClassifierWithNoFactories();
        var registry = BuildRegistry(classifier);
        registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        var parent = registry.GetResource(new ResourceKey("notes.md")).Value as IFileResource;
        var sidecar = registry.GetResource(new ResourceKey("notes.md.cel")).Value as IFileResource;

        parent!.FileKind.Should().Be(FileKind.PlainData);
        sidecar!.FileKind.Should().Be(FileKind.Sidecar);
    }

    [Test]
    public void Classify_RegisteredStandaloneCel_IsStandalone()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "page.webview.cel"),
            "source_url = \"https://example.com\"\n");

        var editorRegistry = Substitute.For<IDocumentEditorRegistry>();
        editorRegistry.IsExtensionSupported(".webview.cel").Returns(true);

        var classifier = ResourceClassifierTestHelper.BuildClassifier(editorRegistry);
        var registry = BuildRegistry(classifier);
        registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        var file = registry.GetResource(new ResourceKey("page.webview.cel")).Value as IFileResource;
        file!.FileKind.Should().Be(FileKind.Standalone);
    }

    [Test]
    public void Classify_ParentlessUnregisteredCel_IsOrphan()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "lonely.cel"), "tags = []\n");

        var classifier = ResourceClassifierTestHelper.BuildClassifierWithNoFactories();
        var registry = BuildRegistry(classifier);
        registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        var file = registry.GetResource(new ResourceKey("lonely.cel")).Value as IFileResource;
        file!.FileKind.Should().Be(FileKind.Orphan);
    }

    [Test]
    public void Classify_DoubleCelExtension_IsInvalidSidecar()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "stray.cel.cel"), "broken\n");

        var classifier = ResourceClassifierTestHelper.BuildClassifierWithNoFactories();
        var registry = BuildRegistry(classifier);
        registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        var file = registry.GetResource(new ResourceKey("stray.cel.cel")).Value as IFileResource;
        file!.FileKind.Should().Be(FileKind.InvalidSidecar);
    }

    private ResourceRegistry BuildRegistry(ResourceClassifier classifier)
    {
        var registry = new ResourceRegistry(
            Substitute.For<ILogger<ResourceRegistry>>(),
            new Celbridge.Messaging.Services.MessengerService(),
            ProjectTreeBuilderTestHelper.Build(new Celbridge.UserInterface.Services.FileIconService()),
            classifier,
            new RootHandlerRegistry(),
            TestFileSystem.CreateLocal());
        registry.InitializeProjectRoot(_projectFolderPath);
        return registry;
    }
}
