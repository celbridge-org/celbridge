using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests that the registry pairing pass classifies sidecar files cleanly into
/// Healthy or Broken across a range of input shapes, and that broken bytes are
/// never modified on disk. The user is responsible for repairing broken
/// sidecars by hand.
/// </summary>
[TestFixture]
public class SidecarClassificationTests
{
    private string _projectFolderPath = null!;
    private ResourceRegistry _registry = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(SidecarClassificationTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        _registry = new ResourceRegistry(
            Substitute.For<ILogger<ResourceRegistry>>(),
            new MessengerService(),
            ProjectTreeBuilderTestHelper.Build(_projectFolderPath),
            ResourceClassifierTestHelper.BuildClassifierWithNoFactories(),
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

    private SidecarLink? GetParentSidecar(string parentName)
    {
        var resource = _registry.GetResource(new ResourceKey(parentName)).Value as IFileResource;
        return resource!.Sidecar;
    }

    [Test]
    public async Task MalformedTomlPrefix_ClassifiedAsBroken_BytesUntouched()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png"), "data");
        var sidecarPath = Path.Combine(_projectFolderPath, "foo.png.cel");
        var originalContent = "not = valid = toml = !!!";
        File.WriteAllText(sidecarPath, originalContent);

        (await _registry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        GetParentSidecar("foo.png")!.Status.Should().Be(CelFileStatus.Broken);
        File.ReadAllText(sidecarPath).Should().Be(originalContent);
    }

    [Test]
    public async Task UnterminatedTomlString_ClassifiedAsBroken_BytesUntouched()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png"), "data");
        var sidecarPath = Path.Combine(_projectFolderPath, "foo.png.cel");
        var originalContent = "key = \"unterminated\nstring = true\n";
        File.WriteAllText(sidecarPath, originalContent);

        (await _registry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        GetParentSidecar("foo.png")!.Status.Should().Be(CelFileStatus.Broken);
        File.ReadAllText(sidecarPath).Should().Be(originalContent);
    }

    [Test]
    public async Task MergeConflictMarkers_ClassifiedAsBroken_BytesUntouched()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png"), "data");
        var sidecarPath = Path.Combine(_projectFolderPath, "foo.png.cel");
        var originalContent =
            "<<<<<<< HEAD\n" +
            "tags = [\"theirs\"]\n" +
            "=======\n" +
            "tags = [\"ours\"]\n" +
            ">>>>>>> branch\n";
        File.WriteAllText(sidecarPath, originalContent);

        (await _registry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        GetParentSidecar("foo.png")!.Status.Should().Be(CelFileStatus.Broken);
        File.ReadAllText(sidecarPath).Should().Be(originalContent);
        _registry.GetSidecarReport().Broken.Should().Contain(new ResourceKey("foo.png.cel"));
    }

    [Test]
    public async Task DuplicateBlockNames_ClassifiedAsBroken_BytesUntouched()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png"), "data");
        var sidecarPath = Path.Combine(_projectFolderPath, "foo.png.cel");
        var originalContent =
            "tags = [\"x\"]\n" +
            "+++ \"a\"\nfirst\n" +
            "+++ \"a\"\nsecond";
        File.WriteAllText(sidecarPath, originalContent);

        (await _registry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        GetParentSidecar("foo.png")!.Status.Should().Be(CelFileStatus.Broken);
        File.ReadAllText(sidecarPath).Should().Be(originalContent);
    }

    [Test]
    public async Task BomAndCrlf_ClassifiedAsHealthy()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png"), "data");
        var sidecarPath = Path.Combine(_projectFolderPath, "foo.png.cel");
        var content = "﻿key = \"value\"\r\n";
        File.WriteAllText(sidecarPath, content);

        (await _registry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        GetParentSidecar("foo.png")!.Status.Should().Be(CelFileStatus.Healthy);
    }

    [Test]
    public async Task ProjectLoads_EvenWhenSidecarStateIsBroken()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "good.png"), "data");
        File.WriteAllText(Path.Combine(_projectFolderPath, "good.png.cel"),
            "tags = [\"x\"]\n");

        File.WriteAllText(Path.Combine(_projectFolderPath, "bad.png"), "data");
        File.WriteAllText(Path.Combine(_projectFolderPath, "bad.png.cel"),
            "malformed = \n");

        var result = await _registry.UpdateResourceRegistryAsync();
        result.IsSuccess.Should().BeTrue();

        GetParentSidecar("good.png")!.Status.Should().Be(CelFileStatus.Healthy);
        GetParentSidecar("bad.png")!.Status.Should().Be(CelFileStatus.Broken);
    }
}
