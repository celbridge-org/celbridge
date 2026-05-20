using Celbridge.Explorer.Services;
using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.UserInterface.Services;

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
            new FileIconService());
        _registry.ProjectFolderPath = _projectFolderPath;
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

    private SidecarInfo? GetParentSidecar(string parentName)
    {
        var resource = _registry.GetResource(new ResourceKey(parentName)).Value as IFileResource;
        return resource!.Sidecar;
    }

    [Test]
    public void NoFences_ClassifiedAsBroken_BytesUntouched()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png"), "data");
        var sidecarPath = Path.Combine(_projectFolderPath, "foo.png.cel");
        var originalContent = "loose body text with no fences at all";
        File.WriteAllText(sidecarPath, originalContent);

        _registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        GetParentSidecar("foo.png")!.Status.Should().Be(SidecarStatus.Broken);
        File.ReadAllText(sidecarPath).Should().Be(originalContent);
    }

    [Test]
    public void MissingClosingFence_ClassifiedAsBroken_BytesUntouched()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png"), "data");
        var sidecarPath = Path.Combine(_projectFolderPath, "foo.png.cel");
        var originalContent = "+++\nkey = \"value\"\nno closing fence";
        File.WriteAllText(sidecarPath, originalContent);

        _registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        GetParentSidecar("foo.png")!.Status.Should().Be(SidecarStatus.Broken);
        File.ReadAllText(sidecarPath).Should().Be(originalContent);
    }

    [Test]
    public void MalformedToml_ClassifiedAsBroken_BytesUntouched()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png"), "data");
        var sidecarPath = Path.Combine(_projectFolderPath, "foo.png.cel");
        var originalContent = "+++\nkey = \"unterminated\nstring = true\n+++\nbody";
        File.WriteAllText(sidecarPath, originalContent);

        _registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        GetParentSidecar("foo.png")!.Status.Should().Be(SidecarStatus.Broken);
        File.ReadAllText(sidecarPath).Should().Be(originalContent);
    }

    [Test]
    public void MergeConflictMarkers_ClassifiedAsBroken_BytesUntouched()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png"), "data");
        var sidecarPath = Path.Combine(_projectFolderPath, "foo.png.cel");
        var originalContent =
            "+++\n" +
            "<<<<<<< HEAD\n" +
            "tags = [\"theirs\"]\n" +
            "=======\n" +
            "tags = [\"ours\"]\n" +
            ">>>>>>> branch\n" +
            "+++\n";
        File.WriteAllText(sidecarPath, originalContent);

        _registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        GetParentSidecar("foo.png")!.Status.Should().Be(SidecarStatus.Broken);
        File.ReadAllText(sidecarPath).Should().Be(originalContent);
        _registry.GetSidecarReport().Broken.Should().Contain(new ResourceKey("foo.png.cel"));
    }

    [Test]
    public void BomAndCrlf_ClassifiedAsHealthy()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.png"), "data");
        var sidecarPath = Path.Combine(_projectFolderPath, "foo.png.cel");
        var content = "﻿+++\r\nkey = \"value\"\r\n+++\r\n";
        File.WriteAllText(sidecarPath, content);

        _registry.UpdateResourceRegistry().IsSuccess.Should().BeTrue();

        GetParentSidecar("foo.png")!.Status.Should().Be(SidecarStatus.Healthy);
    }

    [Test]
    public void ProjectLoads_EvenWhenSidecarStateIsBroken()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "good.png"), "data");
        File.WriteAllText(Path.Combine(_projectFolderPath, "good.png.cel"),
            "+++\ntags = [\"x\"]\n+++\n");

        File.WriteAllText(Path.Combine(_projectFolderPath, "bad.png"), "data");
        File.WriteAllText(Path.Combine(_projectFolderPath, "bad.png.cel"),
            "+++\nmalformed = \n+++\n");

        var result = _registry.UpdateResourceRegistry();
        result.IsSuccess.Should().BeTrue();

        GetParentSidecar("good.png")!.Status.Should().Be(SidecarStatus.Healthy);
        GetParentSidecar("bad.png")!.Status.Should().Be(SidecarStatus.Broken);
    }
}
