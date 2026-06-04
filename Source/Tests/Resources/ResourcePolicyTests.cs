using Celbridge.FileSystem;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Resources.Services;

namespace Celbridge.Tests.Resources;

[TestFixture]
public class ResourcePolicyTests
{
    private const string ProjectFolderPath = @"C:\fake\project";

    // Builds a policy over an in-memory [resources] section and an optional
    // ignore-file content. When ignoreFileContent is null the ignore-file read
    // fails, modelling a project with no ignore-file (empty ignore set).
    private static ResourcePolicy BuildPolicy(ResourcesSection? section = null, string? ignoreFileContent = null)
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

        var fileSystem = Substitute.For<ILocalFileSystem>();
        var readResult = ignoreFileContent is null
            ? Result<string>.Fail("ignore-file not found")
            : Result<string>.Ok(ignoreFileContent);
        fileSystem.ReadAllTextAsync(Arg.Any<string>()).Returns(Task.FromResult(readResult));

        var policy = new ResourcePolicy(projectService, fileSystem);
        policy.InitializeAsync().GetAwaiter().GetResult();
        return policy;
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
    public void DefaultPolicy_DeniesGitFolder()
    {
        // .git is system-deny (never written into a .gitignore) so it is hidden
        // even with no ignore-file present.
        var policy = BuildPolicy();
        policy.Evaluate(new ResourceKey(".git"), ResourceAction.List, isFolder: true).IsFailure.Should().BeTrue();
        policy.Evaluate(new ResourceKey(".git/config"), ResourceAction.Read).IsFailure.Should().BeTrue();
    }

    [Test]
    public void DefaultPolicy_WithNoIgnoreFile_AllowsDotFiles()
    {
        // Phase 4 has no blanket leading-dot rule; visibility comes from the
        // ignore-file. With none present, useful dotfiles stay visible.
        var policy = BuildPolicy();
        policy.Evaluate(new ResourceKey(".editorconfig"), ResourceAction.List).IsSuccess.Should().BeTrue();
        policy.Evaluate(new ResourceKey("bin"), ResourceAction.List, isFolder: true).IsSuccess.Should().BeTrue();
    }

    [Test]
    public void DefaultPolicy_AllowsProjectFile()
    {
        var policy = BuildPolicy();
        var result = policy.Evaluate(new ResourceKey("myproject.celbridge"), ResourceAction.Write);
        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public void SystemAllow_ProtectsProjectFile_EvenWhenIgnored()
    {
        var policy = BuildPolicy(ignoreFileContent: "*.celbridge\n");
        var result = policy.Evaluate(new ResourceKey("myproject.celbridge"), ResourceAction.Write);
        result.IsSuccess.Should().BeTrue();
        policy.Evaluate(new ResourceKey("myproject.celbridge"), ResourceAction.List).IsSuccess.Should().BeTrue();
    }

    [Test]
    public void IgnoreFile_HidesMatchedPaths()
    {
        var policy = BuildPolicy(ignoreFileContent: "bin/\n*.log\n");

        policy.Evaluate(new ResourceKey("notes.md"), ResourceAction.List).IsSuccess.Should().BeTrue();
        policy.Evaluate(new ResourceKey("bin"), ResourceAction.List, isFolder: true).IsFailure.Should().BeTrue();
        policy.Evaluate(new ResourceKey("bin/app.exe"), ResourceAction.List).IsFailure.Should().BeTrue();
        policy.Evaluate(new ResourceKey("run.log"), ResourceAction.List).IsFailure.Should().BeTrue();
        policy.Evaluate(new ResourceKey("logs/run.log"), ResourceAction.List).IsFailure.Should().BeTrue();
    }

    [Test]
    public void IgnoredDirectory_NegatedChild_StaysIgnored()
    {
        // gitignore parent-pruning: a file re-included with '!' under an excluded
        // directory does not resurface, matching git's own behaviour. The Add
        // list is the supported way to bring it back into the resource set.
        var policy = BuildPolicy(ignoreFileContent: "build/\n!build/keep.txt\n");

        policy.Evaluate(new ResourceKey("build"), ResourceAction.List, isFolder: true).IsFailure.Should().BeTrue();
        policy.Evaluate(new ResourceKey("build/keep.txt"), ResourceAction.List).IsFailure.Should().BeTrue();
    }

    [Test]
    public void Add_ResurfacesAnIgnoredFile_Granularly()
    {
        // The .mcp.json-style granular add: the ignore-file hides every dotfile,
        // and add brings back exactly one without resurfacing the rest.
        var section = new ResourcesSection
        {
            Add = new[] { ".mcp.json" },
        };
        var policy = BuildPolicy(section, ignoreFileContent: ".*\n");

        policy.Evaluate(new ResourceKey(".mcp.json"), ResourceAction.List).IsSuccess.Should().BeTrue();
        policy.Evaluate(new ResourceKey(".env"), ResourceAction.List).IsFailure.Should().BeTrue();
    }

    [Test]
    public void Remove_BeatsAdd()
    {
        var section = new ResourcesSection
        {
            Add = new[] { "secret.txt" },
            Remove = new[] { "secret.txt" },
        };
        var policy = BuildPolicy(section, ignoreFileContent: "secret.txt\n");

        var result = policy.Evaluate(new ResourceKey("secret.txt"), ResourceAction.List);
        result.IsFailure.Should().BeTrue();
        result.HasException<PolicyDenialError>().Should().BeTrue();
    }

    [Test]
    public void Remove_DropsAVisibleResource()
    {
        var section = new ResourcesSection
        {
            Remove = new[] { "drafts" },
        };
        var policy = BuildPolicy(section);

        policy.Evaluate(new ResourceKey("drafts"), ResourceAction.List, isFolder: true).IsFailure.Should().BeTrue();
        policy.Evaluate(new ResourceKey("drafts/notes.md"), ResourceAction.List).IsFailure.Should().BeTrue();
        policy.Evaluate(new ResourceKey("notes.md"), ResourceAction.List).IsSuccess.Should().BeTrue();
    }

    [Test]
    public void EmptyIgnoreFile_DisablesBaseline()
    {
        // ignore-file = "" means nothing is ignored: everything below the system
        // tier is a candidate resource, even content the default file would hide.
        var section = new ResourcesSection
        {
            IgnoreFile = string.Empty,
        };
        var policy = BuildPolicy(section, ignoreFileContent: "bin/\n");

        policy.Evaluate(new ResourceKey("bin"), ResourceAction.List, isFolder: true).IsSuccess.Should().BeTrue();
        policy.Evaluate(new ResourceKey(".celbridge"), ResourceAction.List, isFolder: true).IsFailure.Should().BeTrue();
    }

    [Test]
    public void Lock_DeniesWriteAllowsRead()
    {
        var section = new ResourcesSection
        {
            Lock = new[] { "assets/**" },
        };
        var policy = BuildPolicy(section);

        var writeResult = policy.Evaluate(new ResourceKey("assets/logo.png"), ResourceAction.Write);
        writeResult.IsFailure.Should().BeTrue();
        writeResult.HasException<PolicyDenialError>().Should().BeTrue();

        var readResult = policy.Evaluate(new ResourceKey("assets/logo.png"), ResourceAction.Read);
        readResult.IsSuccess.Should().BeTrue();
    }

    [Test]
    public void AddBeneathIgnoredFolder_AllowsFolderDescent()
    {
        // ignore-file hides Python/.venv but add targets paths beneath it. The
        // ignored folder (and its ancestor) must be listable so the registry walk
        // descends to the add target; a sibling ignored folder stays hidden.
        var section = new ResourcesSection
        {
            Add = new[] { "Python/.venv/**" },
        };
        var policy = BuildPolicy(section, ignoreFileContent: "Python/.venv/\nPython/cache/\n");

        policy.Evaluate(new ResourceKey("Python"), ResourceAction.List, isFolder: true).IsSuccess.Should().BeTrue();
        policy.Evaluate(new ResourceKey("Python/.venv"), ResourceAction.List, isFolder: true).IsSuccess.Should().BeTrue();
        policy.Evaluate(new ResourceKey("Python/.venv/lib/site.py"), ResourceAction.List).IsSuccess.Should().BeTrue();
        policy.Evaluate(new ResourceKey("Python/cache"), ResourceAction.List, isFolder: true).IsFailure.Should().BeTrue();
    }

    [Test]
    public void RealisticIgnoreFile_HidesNoise_KeepsUsefulDotfiles()
    {
        // A realistic specific-noise ignore-file (the shape the templates ship)
        // excludes build output and OS cruft while leaving useful dotfiles
        // tracked and visible. There is no blanket leading-dot rule.
        const string ignoreContent =
            "bin/\nobj/\nnode_modules/\n__pycache__/\n*.pyc\n.env\n.DS_Store\n";
        var policy = BuildPolicy(ignoreFileContent: ignoreContent);

        policy.Evaluate(new ResourceKey("bin"), ResourceAction.List, isFolder: true).IsFailure.Should().BeTrue();
        policy.Evaluate(new ResourceKey("obj"), ResourceAction.List, isFolder: true).IsFailure.Should().BeTrue();
        policy.Evaluate(new ResourceKey("node_modules"), ResourceAction.List, isFolder: true).IsFailure.Should().BeTrue();
        policy.Evaluate(new ResourceKey("src/__pycache__"), ResourceAction.List, isFolder: true).IsFailure.Should().BeTrue();
        policy.Evaluate(new ResourceKey("module.pyc"), ResourceAction.List).IsFailure.Should().BeTrue();
        policy.Evaluate(new ResourceKey(".env"), ResourceAction.List).IsFailure.Should().BeTrue();
        policy.Evaluate(new ResourceKey(".DS_Store"), ResourceAction.List).IsFailure.Should().BeTrue();

        policy.Evaluate(new ResourceKey(".editorconfig"), ResourceAction.List).IsSuccess.Should().BeTrue();
        policy.Evaluate(new ResourceKey(".github/workflows/ci.yml"), ResourceAction.List).IsSuccess.Should().BeTrue();
        policy.Evaluate(new ResourceKey("src/main.py"), ResourceAction.List).IsSuccess.Should().BeTrue();
    }

    [Test]
    public void NonProjectRoot_AlwaysAllowed()
    {
        var policy = BuildPolicy();

        policy.Evaluate(new ResourceKey("temp:file.txt"), ResourceAction.Read).IsSuccess.Should().BeTrue();
        policy.Evaluate(new ResourceKey("logs:run.log"), ResourceAction.Write).IsSuccess.Should().BeTrue();
    }
}
