using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Direct unit tests for the resource classification pass: parent pairing,
/// parentless orphan recognition, FileKind stamping, and the broken / healthy
/// split. Tests set up real files on disk because the classifier reads
/// sidecar bytes to drive SidecarHelper.Inspect.
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
    public async Task ParentlessCelWithMultiPartExtension_IsOrphan()
    {
        // The .cel namespace is reserved for sidecars. A parentless .cel with
        // any extension shape is an orphan, including multi-part forms that a
        // document type might otherwise have claimed.
        File.WriteAllText(Path.Combine(_projectFolderPath, "feature.note.cel"),
            "[note]\ntitle = \"Hello\"\n");

        var classifier = ResourceClassifierTestHelper.BuildClassifier();
        var registry = BuildRegistry(classifier);
        (await registry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        var file = registry.GetResource(new ResourceKey("feature.note.cel")).Value as IFileResource;
        file!.FileKind.Should().Be(FileKind.Orphan);

        var report = registry.GetSidecarReport();
        report.Orphan.Should().Contain(new ResourceKey("feature.note.cel"));
    }

    [Test]
    public async Task NestedFolders_PairCorrectly_AndReportUsesRelativeKeys()
    {
        // Make sure the pairing pass walks nested folders and produces project-
        // relative keys (not just leaf names). Catches a regression where the
        // service mistakenly built keys from leaf-only names.
        var sub = Path.Combine(_projectFolderPath, "subfolder");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "note.md"), "body");
        File.WriteAllText(Path.Combine(sub, "note.md.cel"), "_tags = [\"meeting\"]\n");

        var classifier = ResourceClassifierTestHelper.BuildClassifier();
        var registry = BuildRegistry(classifier);
        (await registry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        var noteResource = registry.GetResource(new ResourceKey("subfolder/note.md")).Value as IFileResource;
        noteResource!.Sidecar.Should().NotBeNull();
        noteResource.Sidecar!.Key.Should().Be(new ResourceKey("subfolder/note.md.cel"));
        noteResource.Sidecar.Status.Should().Be(CelParseStatus.Healthy);

        registry.GetSidecarReport()
            .Healthy.Should().Contain(new ResourceKey("subfolder/note.md.cel"));
    }

    [Test]
    public async Task EmptyTree_ProducesEmptyReport()
    {
        var classifier = ResourceClassifierTestHelper.BuildClassifier();
        var registry = BuildRegistry(classifier);
        (await registry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        var report = registry.GetSidecarReport();
        report.Healthy.Should().BeEmpty();
        report.Broken.Should().BeEmpty();
        report.Orphan.Should().BeEmpty();
    }

    [Test]
    public async Task Classify_PlainDataFileWithoutSidecar_IsPlainData()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md"), "# Notes\n");

        var classifier = ResourceClassifierTestHelper.BuildClassifier();
        var registry = BuildRegistry(classifier);
        (await registry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        var file = registry.GetResource(new ResourceKey("notes.md")).Value as IFileResource;
        file!.FileKind.Should().Be(FileKind.PlainData);
    }

    [Test]
    public async Task Classify_PairedSidecarAndParent_AssignsExpectedKinds()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md"), "# Notes\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.md.cel"), "_tags = []\n");

        var classifier = ResourceClassifierTestHelper.BuildClassifier();
        var registry = BuildRegistry(classifier);
        (await registry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        var parent = registry.GetResource(new ResourceKey("notes.md")).Value as IFileResource;
        var sidecar = registry.GetResource(new ResourceKey("notes.md.cel")).Value as IFileResource;

        parent!.FileKind.Should().Be(FileKind.PlainData);
        sidecar!.FileKind.Should().Be(FileKind.Sidecar);
    }

    [Test]
    public async Task Classify_ParentlessCel_IsOrphan()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "lonely.cel"), "_tags = []\n");

        var classifier = ResourceClassifierTestHelper.BuildClassifier();
        var registry = BuildRegistry(classifier);
        (await registry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        var file = registry.GetResource(new ResourceKey("lonely.cel")).Value as IFileResource;
        file!.FileKind.Should().Be(FileKind.Orphan);
    }

    [Test]
    public async Task Classify_DoubleCelExtension_IsInvalidSidecar()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "stray.cel.cel"), "broken\n");

        var classifier = ResourceClassifierTestHelper.BuildClassifier();
        var registry = BuildRegistry(classifier);
        (await registry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        var file = registry.GetResource(new ResourceKey("stray.cel.cel")).Value as IFileResource;
        file!.FileKind.Should().Be(FileKind.InvalidSidecar);
    }

    private ResourceRegistry BuildRegistry(ResourceClassifier classifier)
    {
        var registry = new ResourceRegistry(
            Substitute.For<ILogger<ResourceRegistry>>(),
            new Celbridge.Messaging.Services.MessengerService(),
            ProjectTreeBuilderTestHelper.Build(_projectFolderPath, new Celbridge.UserInterface.Services.IconService()),
            classifier,
            new RootHandlerRegistry(),
            TestFileSystem.CreateLocal());
        registry.InitializeProjectRoot(_projectFolderPath);
        return registry;
    }
}
