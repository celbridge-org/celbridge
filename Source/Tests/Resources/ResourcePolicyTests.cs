using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Utilities;

namespace Celbridge.Tests.Resources;

[TestFixture]
public class ResourcePolicyTests
{
    private const string ProjectFolderPath = @"C:\fake\project";

    // Builds a policy over in-memory Explorer and search lists.
    private static ResourcePolicy BuildPolicy(string[]? explorerExclude = null, string[]? searchExclude = null)
    {
        var config = new ProjectConfig
        {
            Explorer = new ExplorerSection { Exclude = explorerExclude ?? [] },
            Search = new SearchSection { Exclude = searchExclude ?? [] },
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
    public void Evaluate_AllowsAFolderNamedCelbridge()
    {
        // Celbridge keeps project data in the application data folder. A .celbridge folder left in the project
        // by an earlier version is an ordinary folder.
        var policy = BuildPolicy();

        policy.Evaluate(new ResourceKey(".celbridge"), ResourceAction.Read, isFolder: true).IsSuccess.Should().BeTrue();
        policy.Evaluate(new ResourceKey(".celbridge/state.json"), ResourceAction.Write).IsSuccess.Should().BeTrue();
    }

    [Test]
    public void Evaluate_DeniesGitFolder()
    {
        var policy = BuildPolicy();

        policy.Evaluate(new ResourceKey(".git"), ResourceAction.Read, isFolder: true).IsFailure.Should().BeTrue();
        policy.Evaluate(new ResourceKey(".git/config"), ResourceAction.Read).IsFailure.Should().BeTrue();
    }

    [Test]
    public void Evaluate_DeniesGitFolder_InAnyCase()
    {
        var policy = BuildPolicy();

        policy.Evaluate(new ResourceKey(".GIT"), ResourceAction.Read, isFolder: true).IsFailure.Should().BeTrue();
        policy.Evaluate(new ResourceKey(".Git/config"), ResourceAction.Read).IsFailure.Should().BeTrue();
        policy.Evaluate(new ResourceKey("docs/.GIT/config"), ResourceAction.Write).IsFailure.Should().BeTrue();
    }

    [Test]
    public void Evaluate_DenialInsideGitFolder_NamesTheGitRule()
    {
        var policy = BuildPolicy();

        var result = policy.Evaluate(new ResourceKey(".git/hooks/pre-commit"), ResourceAction.Write);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("pattern '.git'");
    }

    [Test]
    public void Evaluate_IgnoresTheExplorerAndSearchLists()
    {
        // The two configured lists are ergonomic, not access control: nothing a project
        // writes can deny a read or a write.
        var policy = BuildPolicy(explorerExclude: new[] { "secret.txt" }, searchExclude: new[] { "node_modules" });

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
    public void IsExplorerExcluded_MatchesPatternsAndTheirSubtrees()
    {
        var policy = BuildPolicy(explorerExclude: new[] { ".gitignore", "drafts" });

        policy.IsExplorerExcluded(new ResourceKey(".gitignore"), isFolder: false).Should().BeTrue();
        policy.IsExplorerExcluded(new ResourceKey("drafts"), isFolder: true).Should().BeTrue();
        policy.IsExplorerExcluded(new ResourceKey("drafts/notes.md"), isFolder: false).Should().BeTrue();
        policy.IsExplorerExcluded(new ResourceKey("notes.md"), isFolder: false).Should().BeFalse();
    }

    [Test]
    public void IsExplorerExcluded_ReadsTheListAsGitignoreLines()
    {
        var policy = BuildPolicy(explorerExclude: new[] { "*.log", "!keep.log", "/build", "# draft", "data?.csv" });

        policy.IsExplorerExcluded(new ResourceKey("logs/run.log"), isFolder: false).Should().BeTrue();
        policy.IsExplorerExcluded(new ResourceKey("logs/keep.log"), isFolder: false).Should().BeFalse();
        policy.IsExplorerExcluded(new ResourceKey("build"), isFolder: true).Should().BeTrue();
        policy.IsExplorerExcluded(new ResourceKey("src/build"), isFolder: true).Should().BeFalse();
        policy.IsExplorerExcluded(new ResourceKey("# draft"), isFolder: false).Should().BeFalse();
        policy.IsExplorerExcluded(new ResourceKey("data1.csv"), isFolder: false).Should().BeTrue();
    }

    [Test]
    public void IsExplorerExcluded_MatchesExactLetterCase()
    {
        var policy = BuildPolicy(explorerExclude: new[] { "Drafts" });

        policy.IsExplorerExcluded(new ResourceKey("Drafts"), isFolder: true).Should().BeTrue();
        policy.IsExplorerExcluded(new ResourceKey("drafts"), isFolder: true).Should().BeFalse();
    }

    [Test]
    public void IsExplorerExcluded_ReportsFalse_WithNoPatterns()
    {
        var policy = BuildPolicy();

        policy.IsExplorerExcluded(new ResourceKey(".gitignore"), isFolder: false).Should().BeFalse();
        policy.IsExplorerExcluded(new ResourceKey("bin"), isFolder: true).Should().BeFalse();
    }

    [Test]
    public void IsSearchExcluded_TakesTheWholeSubtree_ForEveryPatternShape()
    {
        // A pattern that matches a folder matches everything beneath it, whether it is
        // written bare, with a leading wildcard, or as a path.
        var policy = BuildPolicy(searchExclude: new[] { "bin", "**/cache", "src/obj" });

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
        var policy = BuildPolicy(searchExclude: new[] { "dist/" });

        policy.IsSearchExcluded(new ResourceKey("dist"), isFolder: true).Should().BeTrue();
        policy.IsSearchExcluded(new ResourceKey("dist/bundle.js"), isFolder: false).Should().BeTrue();
        policy.IsSearchExcluded(new ResourceKey("dist"), isFolder: false).Should().BeFalse();
    }

    [Test]
    public void ProjectPatterns_DoNotMatchOtherRoots()
    {
        var policy = BuildPolicy(explorerExclude: new[] { "run.log" }, searchExclude: new[] { "run.log" });

        policy.IsExplorerExcluded(new ResourceKey("logs:run.log"), isFolder: false).Should().BeFalse();
        policy.IsSearchExcluded(new ResourceKey("logs:run.log"), isFolder: false).Should().BeFalse();
    }

    [Test]
    public void EachList_AnswersOnlyItsOwnQuestion()
    {
        var policy = BuildPolicy(explorerExclude: new[] { ".gitignore" }, searchExclude: new[] { "dist" });

        policy.IsExplorerExcluded(new ResourceKey("dist"), isFolder: true).Should().BeFalse();
        policy.IsSearchExcluded(new ResourceKey(".gitignore"), isFolder: false).Should().BeFalse();
    }
}
