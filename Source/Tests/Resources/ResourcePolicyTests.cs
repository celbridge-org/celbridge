using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Utilities;

namespace Celbridge.Tests.Resources;

[TestFixture]
public class ResourcePolicyTests
{
    private const string ProjectFolderPath = @"C:\fake\project";

    // Builds a policy over an in-memory [celbridge.resources] section.
    private static ResourcePolicy BuildPolicy(ResourcesSection? section = null)
    {
        var config = new ProjectConfig
        {
            Resources = section ?? new ResourcesSection(),
        };
        var project = Substitute.For<IProject>();
        project.Config.Returns(config);
        project.ProjectFolderPath.Returns(ProjectFolderPath);

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        return new ResourcePolicy(projectService);
    }

    [Test]
    public void Evaluate_AllowsRegularFile()
    {
        var policy = BuildPolicy();

        policy.Evaluate(new ResourceKey("notes/todo.md"), ResourceAction.Read).IsSuccess.Should().BeTrue();
        policy.Evaluate(new ResourceKey("notes/todo.md"), ResourceAction.Write).IsSuccess.Should().BeTrue();
    }

    [Test]
    public void Evaluate_DeniesCelbridgeMetadataFolder()
    {
        var policy = BuildPolicy();

        var result = policy.Evaluate(new ResourceKey(".celbridge"), ResourceAction.Read, isFolder: true);
        result.IsFailure.Should().BeTrue();
        result.HasException<PolicyDenialError>().Should().BeTrue();

        policy.Evaluate(new ResourceKey(".celbridge/state.json"), ResourceAction.Write).IsFailure.Should().BeTrue();
    }

    [Test]
    public void Evaluate_DeniesGitFolder()
    {
        var policy = BuildPolicy();

        policy.Evaluate(new ResourceKey(".git"), ResourceAction.Read, isFolder: true).IsFailure.Should().BeTrue();
        policy.Evaluate(new ResourceKey(".git/config"), ResourceAction.Read).IsFailure.Should().BeTrue();
    }

    [Test]
    public void Evaluate_IgnoresHideAndSearchExclude()
    {
        // The two configured lists are ergonomic, not access control: nothing a project
        // writes can deny a read or a write.
        var section = new ResourcesSection
        {
            Hide = new[] { "secret.txt" },
            SearchExclude = new[] { "node_modules" },
        };
        var policy = BuildPolicy(section);

        policy.Evaluate(new ResourceKey("secret.txt"), ResourceAction.Read).IsSuccess.Should().BeTrue();
        policy.Evaluate(new ResourceKey("secret.txt"), ResourceAction.Write).IsSuccess.Should().BeTrue();
        policy.Evaluate(new ResourceKey("node_modules/pkg/index.js"), ResourceAction.Write).IsSuccess.Should().BeTrue();
    }

    [Test]
    public void Evaluate_AllowsNonProjectRoot()
    {
        var policy = BuildPolicy();

        policy.Evaluate(new ResourceKey("temp:file.txt"), ResourceAction.Read).IsSuccess.Should().BeTrue();
        policy.Evaluate(new ResourceKey("logs:run.log"), ResourceAction.Write).IsSuccess.Should().BeTrue();
    }

    [Test]
    public void IsHidden_MatchesPatternsAndTheirSubtrees()
    {
        var section = new ResourcesSection
        {
            Hide = new[] { ".gitignore", "drafts" },
        };
        var policy = BuildPolicy(section);

        policy.IsHidden(new ResourceKey(".gitignore"), isFolder: false).Should().BeTrue();
        policy.IsHidden(new ResourceKey("drafts"), isFolder: true).Should().BeTrue();
        policy.IsHidden(new ResourceKey("drafts/notes.md"), isFolder: false).Should().BeTrue();
        policy.IsHidden(new ResourceKey("notes.md"), isFolder: false).Should().BeFalse();
    }

    [Test]
    public void IsHidden_ReportsFalse_WithNoPatterns()
    {
        var policy = BuildPolicy();

        policy.IsHidden(new ResourceKey(".gitignore"), isFolder: false).Should().BeFalse();
        policy.IsHidden(new ResourceKey("bin"), isFolder: true).Should().BeFalse();
    }

    [Test]
    public void IsSearchExcluded_TakesTheWholeSubtree_ForEveryPatternShape()
    {
        // A pattern that matches a folder matches everything beneath it, whether it is
        // written bare, with a leading wildcard, or as a path.
        var section = new ResourcesSection
        {
            SearchExclude = new[] { "bin", "**/cache", "src/obj" },
        };
        var policy = BuildPolicy(section);

        policy.IsSearchExcluded(new ResourceKey("bin/app.exe"), isFolder: false).Should().BeTrue();
        policy.IsSearchExcluded(new ResourceKey("src/bin/app.exe"), isFolder: false).Should().BeTrue();
        policy.IsSearchExcluded(new ResourceKey("src/cache/entry.bin"), isFolder: false).Should().BeTrue();
        policy.IsSearchExcluded(new ResourceKey("src/obj/build.log"), isFolder: false).Should().BeTrue();

        policy.IsSearchExcluded(new ResourceKey("src/main.py"), isFolder: false).Should().BeFalse();
        policy.IsSearchExcluded(new ResourceKey("obj/build.log"), isFolder: false).Should().BeFalse();
    }

    [Test]
    public void IsSearchExcluded_FolderOnlyPattern_SkipsAFileOfTheSameName()
    {
        var section = new ResourcesSection
        {
            SearchExclude = new[] { "dist/" },
        };
        var policy = BuildPolicy(section);

        policy.IsSearchExcluded(new ResourceKey("dist"), isFolder: true).Should().BeTrue();
        policy.IsSearchExcluded(new ResourceKey("dist/bundle.js"), isFolder: false).Should().BeTrue();
        policy.IsSearchExcluded(new ResourceKey("dist"), isFolder: false).Should().BeFalse();
    }

    [Test]
    public void ProjectPatterns_DoNotMatchOtherRoots()
    {
        var section = new ResourcesSection
        {
            Hide = new[] { "run.log" },
            SearchExclude = new[] { "run.log" },
        };
        var policy = BuildPolicy(section);

        policy.IsHidden(new ResourceKey("logs:run.log"), isFolder: false).Should().BeFalse();
        policy.IsSearchExcluded(new ResourceKey("logs:run.log"), isFolder: false).Should().BeFalse();
    }

    [Test]
    public void ReservedMatcher_RejectsAFoldersOnlyPattern()
    {
        // Evaluate passes the caller's isFolder hint straight through, so a trailing slash would be
        // written into the rule set and then quietly do nothing. It is refused at compile time instead.
        var compile = () => ResourcePolicy.CompileReservedMatcher(".svn/");

        compile.Should().Throw<ArgumentException>().WithMessage("*folders-only*");
    }

    [Test]
    public void ReservedMatcher_AcceptsTheShapesTheRuleSetUses()
    {
        ResourcePolicy.CompileReservedMatcher(".git").Target.Should().Be(PathMatchTarget.Any);
        ResourcePolicy.CompileReservedMatcher(".git/**").Target.Should().Be(PathMatchTarget.Any);
    }
}
