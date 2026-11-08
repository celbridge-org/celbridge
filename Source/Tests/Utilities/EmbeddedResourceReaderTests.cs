using Celbridge.Projects;
using Celbridge.Utilities;

namespace Celbridge.Tests.Utilities;

[TestFixture]
public class EmbeddedResourceReaderTests
{
    // Read across an assembly boundary, which is what every caller does.
    private static readonly System.Reflection.Assembly ProjectsAssembly = typeof(GitIgnoreWriter).Assembly;

    [Test]
    public void ReadText_EmbeddedResource_ReturnsItsContents()
    {
        var result = EmbeddedResourceReader.ReadText(
            ProjectsAssembly, "Celbridge.Projects.Assets.GitIgnore.txt");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("node_modules/");
    }

    [Test]
    public void ReadText_MissingResource_Fails()
    {
        // A name that does not resolve is a build problem, so it surfaces rather than reading as empty
        // content or as a placeholder that a caller would go on to use.
        var result = EmbeddedResourceReader.ReadText(
            ProjectsAssembly, "Celbridge.Projects.Assets.NoSuchResource.txt");

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("NoSuchResource.txt");
    }

    [Test]
    public void ReadText_EmptyResourceName_Fails()
    {
        var result = EmbeddedResourceReader.ReadText(ProjectsAssembly, string.Empty);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void ReadBytes_EmbeddedResource_ReturnsItsBytes()
    {
        var text = EmbeddedResourceReader.ReadText(ProjectsAssembly, "Celbridge.Projects.Assets.GitIgnore.txt");
        var bytes = EmbeddedResourceReader.ReadBytes(ProjectsAssembly, "Celbridge.Projects.Assets.GitIgnore.txt");

        bytes.IsSuccess.Should().BeTrue();
        System.Text.Encoding.UTF8.GetString(bytes.Value).Should().Contain(text.Value);
    }

    [Test]
    public void ReadBytes_MissingResource_Fails()
    {
        var result = EmbeddedResourceReader.ReadBytes(
            ProjectsAssembly, "Celbridge.Projects.Assets.NoSuchResource.png");

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("NoSuchResource.png");
    }
}
