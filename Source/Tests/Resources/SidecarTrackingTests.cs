using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;

namespace Celbridge.Tests.Resources;

[TestFixture]
public class SidecarTrackingTests
{
    private string _projectFolderPath = null!;
    private ResourceRegistry _registry = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(SidecarTrackingTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        _registry = new ResourceRegistry(
            Substitute.For<ILogger<ResourceRegistry>>(),
            new MessengerService(),
            ProjectTreeBuilderTestHelper.Build(_projectFolderPath),
            ResourceClassifierTestHelper.BuildClassifier(),
            new RootHandlerRegistry(),
            TestFileSystem.CreateLocal());
        _registry.InitializeProjectRoot(_projectFolderPath);
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
    public async Task FileWithNoSidecar_HasNullSidecar()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png"), "fake-png-bytes");

        (await _registry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        var resourceResult = _registry.GetResource(new ResourceKey("foo.png"));
        resourceResult.IsSuccess.Should().BeTrue();
        var fileResource = resourceResult.Value as IFileResource;
        fileResource!.Sidecar.Should().BeNull();
    }

    [Test]
    public async Task HealthySidecar_IsPairedWithStatusHealthy()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png"), "fake-png-bytes");
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png.cel"),
            "tags = [\"meeting\"]\n");

        (await _registry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        var resourceResult = _registry.GetResource(new ResourceKey("foo.png"));
        resourceResult.IsSuccess.Should().BeTrue();
        var fileResource = resourceResult.Value as IFileResource;
        fileResource!.Sidecar.Should().NotBeNull();
        fileResource.Sidecar!.Key.Should().Be(new ResourceKey("foo.png.cel"));
        fileResource.Sidecar.Status.Should().Be(CelFileStatus.Healthy);
    }

    [Test]
    public async Task OrphanSidecar_AppearsInReportOrphan()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png.cel"),
            "tags = [\"x\"]\n");

        (await _registry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        var report = _registry.GetSidecarReport();
        report.Orphan.Should().Contain(new ResourceKey("foo.png.cel"));
    }

    [Test]
    public async Task CelCelFile_AppearsInReportBroken()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png"), "data");
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png.cel"),
            "tags = [\"a\"]\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png.cel.cel"),
            "should = \"not be paired\"\n");

        (await _registry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        var report = _registry.GetSidecarReport();
        report.Broken.Should().Contain(new ResourceKey("foo.png.cel.cel"));

        // foo.png.cel is still healthy and paired with foo.png; the .cel.cel
        // file is not considered its sidecar.
        report.Healthy.Should().Contain(new ResourceKey("foo.png.cel"));
        var fooPng = _registry.GetResource(new ResourceKey("foo.png")).Value as IFileResource;
        fooPng!.Sidecar!.Status.Should().Be(CelFileStatus.Healthy);
    }

    [Test]
    public async Task UnparseableSidecar_AppearsInReportBroken()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png"), "data");
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png.cel"),
            "not = valid = toml = !!!");

        (await _registry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        var report = _registry.GetSidecarReport();
        report.Broken.Should().Contain(new ResourceKey("foo.png.cel"));

        var parent = _registry.GetResource(new ResourceKey("foo.png")).Value as IFileResource;
        parent!.Sidecar!.Status.Should().Be(CelFileStatus.Broken);
    }

    [Test]
    public async Task DeletingSidecar_FlipsParentToNullSidecar()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png"), "data");
        var sidecarPath = Path.Combine(_projectFolderPath, "foo.png.cel");
        File.WriteAllText(sidecarPath, "tags = [\"x\"]\n");

        (await _registry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        var parent1 = _registry.GetResource(new ResourceKey("foo.png")).Value as IFileResource;
        parent1!.Sidecar!.Status.Should().Be(CelFileStatus.Healthy);

        File.Delete(sidecarPath);
        (await _registry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        var parent2 = _registry.GetResource(new ResourceKey("foo.png")).Value as IFileResource;
        parent2!.Sidecar.Should().BeNull();
    }

    [Test]
    public async Task BrokenOrphan_AppearsInBothBrokenAndOrphan()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "lonely.cel"), "loose = invalid toml here = !!!");

        (await _registry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        var report = _registry.GetSidecarReport();
        report.Broken.Should().Contain(new ResourceKey("lonely.cel"));
        report.Orphan.Should().Contain(new ResourceKey("lonely.cel"));
    }

    // Parentless .cel handling and the orphan / invalid-sidecar split live in
    // ResourceClassifierTests, which targets the classifier directly.
}
