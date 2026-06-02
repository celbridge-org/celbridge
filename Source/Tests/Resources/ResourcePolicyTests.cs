using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Resources.Services;

namespace Celbridge.Tests.Resources;

[TestFixture]
public class ResourcePolicyTests
{
    private static ResourcePolicy BuildPolicy(ResourcesSection? section = null)
    {
        var config = new ProjectConfig
        {
            Resources = section ?? new ResourcesSection(),
        };
        var project = Substitute.For<IProject>();
        project.Config.Returns(config);

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        return new ResourcePolicy(projectService);
    }

    [Test]
    public void DefaultPolicy_AllowsRegularFile()
    {
        var policy = BuildPolicy();
        var result = policy.Evaluate(new ResourceKey("notes/todo.md"), ResourceAction.List);
        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public void DefaultPolicy_DeniesCelbridgeMetadataFolder()
    {
        var policy = BuildPolicy();
        var result = policy.Evaluate(new ResourceKey(".celbridge"), ResourceAction.List, isFolder: true);
        result.IsFailure.Should().BeTrue();
        result.HasException<PolicyDenialError>().Should().BeTrue();
    }

    [Test]
    public void DefaultPolicy_DeniesLeadingDotFiles()
    {
        var policy = BuildPolicy();
        var result = policy.Evaluate(new ResourceKey(".env"), ResourceAction.List);
        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void DefaultPolicy_DeniesBinFolder()
    {
        var policy = BuildPolicy();
        var result = policy.Evaluate(new ResourceKey("bin"), ResourceAction.List, isFolder: true);
        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void DefaultPolicy_DeniesDesktopIni()
    {
        // Visibility is pattern-based, not OS-attribute-based, so the main
        // Windows noise file is caught by name (it has no leading dot).
        var policy = BuildPolicy();
        var result = policy.Evaluate(new ResourceKey("desktop.ini"), ResourceAction.List);
        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void DefaultPolicy_AllowsProjectFile()
    {
        var policy = BuildPolicy();
        var result = policy.Evaluate(new ResourceKey("myproject.celbridge"), ResourceAction.Write);
        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public void DefaultPolicy_AllowsProjectFile_EvenUnderRestrictiveInclude()
    {
        var section = new ResourcesSection
        {
            Include = new[] { "src/**" },
        };
        var policy = BuildPolicy(section);
        var result = policy.Evaluate(new ResourceKey("myproject.celbridge"), ResourceAction.Write);
        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public void Include_RestrictsVisibility()
    {
        var section = new ResourcesSection
        {
            Include = new[] { "src/**" },
        };
        var policy = BuildPolicy(section);

        policy.Evaluate(new ResourceKey("src/file.cs"), ResourceAction.List).IsSuccess.Should().BeTrue();
        policy.Evaluate(new ResourceKey("docs/file.md"), ResourceAction.List).IsFailure.Should().BeTrue();
    }

    [Test]
    public void Exclude_SubtractsFromInclude()
    {
        var section = new ResourcesSection
        {
            Include = new[] { "*" },
            Exclude = new[] { "secrets" },
        };
        var policy = BuildPolicy(section);

        policy.Evaluate(new ResourceKey("notes.md"), ResourceAction.List).IsSuccess.Should().BeTrue();
        policy.Evaluate(new ResourceKey("secrets"), ResourceAction.List, isFolder: true).IsFailure.Should().BeTrue();
        policy.Evaluate(new ResourceKey("secrets/api.key"), ResourceAction.List).IsFailure.Should().BeTrue();
    }

    [Test]
    public void Locked_DeniesWriteAllowsRead()
    {
        var section = new ResourcesSection
        {
            Include = new[] { "*" },
            Locked = new[] { "assets/**" },
        };
        var policy = BuildPolicy(section);

        var writeResult = policy.Evaluate(new ResourceKey("assets/logo.png"), ResourceAction.Write);
        writeResult.IsFailure.Should().BeTrue();
        writeResult.HasException<PolicyDenialError>().Should().BeTrue();

        var readResult = policy.Evaluate(new ResourceKey("assets/logo.png"), ResourceAction.Read);
        readResult.IsSuccess.Should().BeTrue();
    }

    [Test]
    public void BuiltinExclude_SuppressedByLiteralInclude()
    {
        var section = new ResourcesSection
        {
            Include = new[] { "*", "node_modules" },
        };
        var policy = BuildPolicy(section);

        var result = policy.Evaluate(new ResourceKey("node_modules"), ResourceAction.List, isFolder: true);
        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public void BuiltinExclude_NotSuppressedByWildcard()
    {
        var section = new ResourcesSection
        {
            Include = new[] { "*" },
        };
        var policy = BuildPolicy(section);

        var result = policy.Evaluate(new ResourceKey("node_modules"), ResourceAction.List, isFolder: true);
        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void NonProjectRoot_AlwaysAllowed()
    {
        var policy = BuildPolicy();

        policy.Evaluate(new ResourceKey("temp:file.txt"), ResourceAction.Read).IsSuccess.Should().BeTrue();
        policy.Evaluate(new ResourceKey("logs:run.log"), ResourceAction.Write).IsSuccess.Should().BeTrue();
    }
}
