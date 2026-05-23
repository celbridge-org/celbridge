using Celbridge.Documents;
using Celbridge.Resources;

namespace Celbridge.Tests.Resources;

[TestFixture]
public class CelFileClassifierTests
{
    private IResourceRegistry _resources = null!;
    private IDocumentEditorRegistry _editors = null!;

    [SetUp]
    public void Setup()
    {
        _resources = Substitute.For<IResourceRegistry>();
        _editors = Substitute.For<IDocumentEditorRegistry>();

        // Default: no parent files exist, no editor factories registered. Tests
        // override the relevant calls to set up their scenarios.
        _resources
            .GetResource(Arg.Any<ResourceKey>())
            .Returns(Result<IResource>.Fail("not found"));
        _editors
            .GetFactoriesForFileExtension(Arg.Any<string>())
            .Returns(Array.Empty<IDocumentEditorFactory>());
    }

    [Test]
    public void Classify_StandaloneWhenMultiPartExtensionRegisteredAndNoParent()
    {
        RegisterExtension(".project.cel");

        var result = CelFileClassifier.Classify(
            new ResourceKey("foo.project.cel"),
            _resources,
            _editors);

        result.Should().Be(CelFileClassification.Standalone);
    }

    [Test]
    public void Classify_SidecarWhenParentExistsEvenIfMultiPartExtensionRegistered()
    {
        RegisterExtension(".project.cel");
        ExistingParentFile("foo.project");

        var result = CelFileClassifier.Classify(
            new ResourceKey("foo.project.cel"),
            _resources,
            _editors);

        result.Should().Be(CelFileClassification.Sidecar);
    }

    [Test]
    public void Classify_SidecarWhenParentExistsAndNoExtensionRegistered()
    {
        ExistingParentFile("foo.png");

        var result = CelFileClassifier.Classify(
            new ResourceKey("foo.png.cel"),
            _resources,
            _editors);

        result.Should().Be(CelFileClassification.Sidecar);
    }

    [Test]
    public void Classify_OrphanWhenNoParentAndNoExtensionRegistered()
    {
        var result = CelFileClassifier.Classify(
            new ResourceKey("foo.png.cel"),
            _resources,
            _editors);

        result.Should().Be(CelFileClassification.Orphan);
    }

    [Test]
    public void Classify_StandaloneForNestedResourceWithRegisteredExtension()
    {
        RegisterExtension(".note.cel");

        var result = CelFileClassifier.Classify(
            new ResourceKey("notes/meeting.note.cel"),
            _resources,
            _editors);

        result.Should().Be(CelFileClassification.Standalone);
    }

    [Test]
    public void Classify_OrphanForBareCelWhenCelNotRegistered()
    {
        var result = CelFileClassifier.Classify(
            new ResourceKey("foo.cel"),
            _resources,
            _editors);

        result.Should().Be(CelFileClassification.Orphan);
    }

    [Test]
    public void Classify_OrphanForKeyNotEndingInCel()
    {
        // Defensive: the classifier is only meaningful for .cel-shaped keys.
        // A non-.cel key is reported as Orphan rather than raising.
        var result = CelFileClassifier.Classify(
            new ResourceKey("foo.png"),
            _resources,
            _editors);

        result.Should().Be(CelFileClassification.Orphan);
    }

    private void RegisterExtension(string extension)
    {
        var factory = Substitute.For<IDocumentEditorFactory>();
        _editors
            .GetFactoriesForFileExtension(extension)
            .Returns(new[] { factory });
    }

    private void ExistingParentFile(string path)
    {
        var parentKey = new ResourceKey($"project:{path}");
        var fileResource = Substitute.For<IFileResource>();
        _resources
            .GetResource(parentKey)
            .Returns(Result<IResource>.Ok(fileResource));
    }
}
