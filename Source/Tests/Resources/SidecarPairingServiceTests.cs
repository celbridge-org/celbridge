using Celbridge.Documents;
using Celbridge.Resources;
using Celbridge.Resources.Services;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Direct unit tests for the sidecar pairing pass: parent pairing, parentless
/// classification (standalone-form vs orphan), and the broken / healthy split.
/// The previous behaviour was tested only end-to-end through ResourceRegistry
/// with a nullable workspace wrapper, which silently disabled the standalone-
/// form recognition in tests; this fixture covers the cross-domain decision
/// directly.
///
/// The pairing service reads sidecar bytes from disk to drive SidecarHelper.Inspect,
/// so tests still set up real files; the value is that they target the service
/// surface rather than the registry.
/// </summary>
[TestFixture]
public class SidecarPairingServiceTests
{
    private string _projectFolderPath = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(SidecarPairingServiceTests),
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
        editorRegistry.GetFactory(
                Arg.Is<ResourceKey>(k => k.ToString().EndsWith("feature.note.cel")))
            .Returns(Result<IDocumentEditorFactory>.Ok(Substitute.For<IDocumentEditorFactory>()));
        editorRegistry.GetFactory(
                Arg.Is<ResourceKey>(k => !k.ToString().EndsWith("feature.note.cel")))
            .Returns(Result<IDocumentEditorFactory>.Fail("no factory"));

        var pairingService = SidecarPairingTestHelper.BuildPairingService(editorRegistry);
        var registry = BuildRegistry(pairingService);
        registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        var report = registry.GetSidecarReport();
        report.Orphan.Should().NotContain(new ResourceKey("feature.note.cel"));
        report.Healthy.Should().Contain(new ResourceKey("feature.note.cel"));
    }

    [Test]
    public void StandaloneCelWithFilenameOnlyRegistration_IsNotReportedAsOrphan()
    {
        // Regression for "package.cel": a filename-only factory registration must
        // also drive standalone classification. Earlier code computed a multi-
        // part suffix and missed the bare-filename case, so every package.cel showed
        // up in the orphan list.
        File.WriteAllText(Path.Combine(_projectFolderPath, "package.cel"),
            "[package]\nid = \"acme\"\nname = \"Acme\"\nversion = \"1.0.0\"\n");

        var editorRegistry = Substitute.For<IDocumentEditorRegistry>();
        editorRegistry.GetFactory(
                Arg.Is<ResourceKey>(k => k.ToString().EndsWith("package.cel")))
            .Returns(Result<IDocumentEditorFactory>.Ok(Substitute.For<IDocumentEditorFactory>()));
        editorRegistry.GetFactory(
                Arg.Is<ResourceKey>(k => !k.ToString().EndsWith("package.cel")))
            .Returns(Result<IDocumentEditorFactory>.Fail("no factory"));

        var pairingService = SidecarPairingTestHelper.BuildPairingService(editorRegistry);
        var registry = BuildRegistry(pairingService);
        registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        var report = registry.GetSidecarReport();
        report.Orphan.Should().NotContain(new ResourceKey("package.cel"));
        report.Healthy.Should().Contain(new ResourceKey("package.cel"));
    }

    [Test]
    public void OrphanCelWithNoFactoryClaim_IsStillReportedAsOrphan()
    {
        // When the editor registry is wired up but no factory claims the
        // file, the .cel is a genuine orphan that the user needs to repair.
        // The registry hookup must not paper over real orphans.
        File.WriteAllText(Path.Combine(_projectFolderPath, "scratch.unknown.cel"),
            "key = \"value\"\n");

        var pairingService = SidecarPairingTestHelper.BuildPairingServiceWithNoFactories();
        var registry = BuildRegistry(pairingService);
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
        editorRegistry.GetFactory(Arg.Any<ResourceKey>())
            .Returns(Result<IDocumentEditorFactory>.Fail("no factory"));

        var pairingService = SidecarPairingTestHelper.BuildPairingService(editorRegistry);
        var registry = BuildRegistry(pairingService);
        registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        editorRegistry.DidNotReceive().GetFactory(new ResourceKey("foo.png.cel"));

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

        var pairingService = SidecarPairingTestHelper.BuildPairingServiceWithNoFactories();
        var registry = BuildRegistry(pairingService);
        registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        var noteResource = registry.GetResource(new ResourceKey("subfolder/note.md")).Value as IFileResource;
        noteResource!.Sidecar.Should().NotBeNull();
        noteResource.Sidecar!.Key.Should().Be(new ResourceKey("subfolder/note.md.cel"));
        noteResource.Sidecar.Status.Should().Be(SidecarStatus.Healthy);

        registry.GetSidecarReport()
            .Healthy.Should().Contain(new ResourceKey("subfolder/note.md.cel"));
    }

    [Test]
    public void EmptyTree_ProducesEmptyReport()
    {
        var pairingService = SidecarPairingTestHelper.BuildPairingServiceWithNoFactories();
        var registry = BuildRegistry(pairingService);
        registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        var report = registry.GetSidecarReport();
        report.Healthy.Should().BeEmpty();
        report.Broken.Should().BeEmpty();
        report.Orphan.Should().BeEmpty();
    }

    private ResourceRegistry BuildRegistry(SidecarPairingService pairingService)
    {
        var registry = new ResourceRegistry(
            Substitute.For<ILogger<ResourceRegistry>>(),
            new Celbridge.Messaging.Services.MessengerService(),
            new ProjectTreeBuilder(new Celbridge.UserInterface.Services.FileIconService()),
            pairingService,
            new RootHandlerRegistry());
        registry.InitializeProjectRoot(_projectFolderPath);
        return registry;
    }
}
