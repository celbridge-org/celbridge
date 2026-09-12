using Celbridge.Commands;
using Celbridge.ProjectSettings.ViewModels;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.ProjectSettings;

/// <summary>
/// Covers the Resources section: what the two pattern blocks write back into the config draft, and which
/// lines are reported as written in a dialect the matcher does not read.
/// </summary>
[TestFixture]
public class ResourcesSectionViewModelTests
{
    private const string ProjectFolderPath = @"C:\fake\project";

    private IProjectService _projectService = null!;
    private ProjectSettingsContext _context = null!;

    [SetUp]
    public void Setup()
    {
        var project = Substitute.For<IProject>();
        project.ProjectFolderPath.Returns(ProjectFolderPath);

        _projectService = Substitute.For<IProjectService>();
        _projectService.CurrentProject.Returns(project);

        _context = new ProjectSettingsContext(
            Substitute.For<IWorkspaceWrapper>(),
            _projectService,
            Substitute.For<ICommandService>(),
            () => { });
    }

    [Test]
    public void Load_PopulatesBothBlocksFromTheConfig()
    {
        var viewModel = CreateViewModel(
            hide: new[] { ".gitignore" },
            searchExclude: new[] { "node_modules", "bin" });

        viewModel.Load();

        viewModel.HidePatternsText.Should().Be(".gitignore");
        viewModel.SearchExcludePatternsText.Should().Be("node_modules\nbin");
    }

    [Test]
    public void Load_DoesNotWriteBackWhatItJustRead()
    {
        var edited = false;
        _context = new ProjectSettingsContext(
            Substitute.For<IWorkspaceWrapper>(),
            _projectService,
            Substitute.For<ICommandService>(),
            () => edited = true);

        var viewModel = CreateViewModel(hide: new[] { ".gitignore" });
        viewModel.Load();

        edited.Should().BeFalse();
    }

    [Test]
    public void EditingABlock_WritesEveryLineIntoTheDraft()
    {
        var viewModel = CreateViewModel(hide: new[] { ".gitignore" });
        viewModel.Load();

        viewModel.HidePatternsText = ".gitignore\ndrafts/**";

        var config = _context.Draft!.ToConfig();
        config.Resources.Hide.Should().Equal(".gitignore", "drafts/**");
    }

    [Test]
    public void EditingABlock_SplitsTheLineEndingTheTextBoxUses()
    {
        // A WinUI text box reports its line breaks as bare carriage returns, so a block split on line
        // feeds alone arrives as one unusable pattern.
        var viewModel = CreateViewModel();
        viewModel.Load();

        viewModel.HidePatternsText = ".claude\r.gitignore";

        var config = _context.Draft!.ToConfig();
        config.Resources.Hide.Should().Equal(".claude", ".gitignore");
    }

    [Test]
    public void EditingABlock_DropsBlankLinesAndPastedCarriageReturns()
    {
        // A block pasted from another project can arrive with any line ending, and a trailing newline is
        // what a user leaves behind rather than an empty pattern.
        var viewModel = CreateViewModel();
        viewModel.Load();

        viewModel.SearchExcludePatternsText = "node_modules\r\n\r\nbin\r\n";

        var config = _context.Draft!.ToConfig();
        config.Resources.SearchExclude.Should().Equal("node_modules", "bin");
    }

    [Test]
    public void ClearingABlock_EmptiesTheList()
    {
        var viewModel = CreateViewModel(searchExclude: new[] { "node_modules" });
        viewModel.Load();

        viewModel.SearchExcludePatternsText = string.Empty;

        var config = _context.Draft!.ToConfig();
        config.Resources.SearchExclude.Should().BeEmpty();
    }

    [Test]
    public void InvalidPattern_IsReportedForTheBlockHoldingIt()
    {
        var viewModel = CreateViewModel();
        viewModel.Load();

        viewModel.HidePatternsText.Should().BeEmpty();
        viewModel.HasInvalidHidePattern.Should().BeFalse("an empty block is unfinished, not wrong");

        viewModel.HidePatternsText = "src/**\nbuild/";
        viewModel.HasInvalidHidePattern.Should().BeFalse();

        // A Windows separator, an anchored path, a negation and a comment are all dialects the matcher
        // does not read.
        viewModel.HidePatternsText = "src/**\n" + @"src\bin";
        viewModel.HasInvalidHidePattern.Should().BeTrue();

        viewModel.HidePatternsText = "/rooted.txt";
        viewModel.HasInvalidHidePattern.Should().BeTrue();

        viewModel.HidePatternsText = "src/**\n!keep.txt";
        viewModel.HasInvalidHidePattern.Should().BeTrue();

        // The box reads like a .gitignore, so a comment is the other thing people reach for.
        viewModel.HidePatternsText = "# Build output\nbin";
        viewModel.HasInvalidHidePattern.Should().BeTrue();

        viewModel.HasInvalidSearchExcludePattern.Should().BeFalse("the other block is judged on its own lines");
    }

    [Test]
    public void GitignoreWildcard_IsReported()
    {
        // The matcher escapes every character but * and /, so a gitignore wildcard is stored as a literal
        // and matches a file nobody has.
        var viewModel = CreateViewModel();
        viewModel.Load();

        viewModel.SearchExcludePatternsText = "log?.txt";
        viewModel.HasInvalidSearchExcludePattern.Should().BeTrue();

        viewModel.SearchExcludePatternsText = "*.[oa]";
        viewModel.HasInvalidSearchExcludePattern.Should().BeTrue();

        viewModel.SearchExcludePatternsText = "**/*.log";
        viewModel.HasInvalidSearchExcludePattern.Should().BeFalse();
    }

    private ResourcesSectionViewModel CreateViewModel(
        string[]? hide = null,
        string[]? searchExclude = null)
    {
        var config = new ProjectConfig
        {
            Resources = new ResourcesSection
            {
                Hide = hide ?? Array.Empty<string>(),
                SearchExclude = searchExclude ?? Array.Empty<string>()
            }
        };

        var project = Substitute.For<IProject>();
        project.Config.Returns(config);
        project.ProjectFolderPath.Returns(ProjectFolderPath);
        _projectService.CurrentProject.Returns(project);

        _context.Draft = new ProjectConfigDraft(config);

        return new ResourcesSectionViewModel(_context);
    }
}
