using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.ProjectSettings.ViewModels;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.ProjectSettings;

/// <summary>
/// Covers the Resources section: what the two pattern blocks and the downloads folder write back into the
/// config draft, which lines are reported as written in a dialect the matcher does not read, and which
/// downloads folders are reported as unusable.
/// </summary>
[TestFixture]
public class ResourcesSectionViewModelTests
{
    private const string ProjectFolderPath = @"C:\fake\project";

    private IProjectService _projectService = null!;
    private IDialogService _dialogService = null!;
    private ProjectSettingsContext _context = null!;
    private IServiceProvider? _previousServiceProvider;

    [SetUp]
    public void Setup()
    {
        // The section titles the folder picker through the localizer, which it acquires from the global
        // ServiceLocator.
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IStringLocalizer>());
        _previousServiceProvider = ServiceLocator.ServiceProvider;
        ServiceLocator.Initialize(services.BuildServiceProvider());

        _dialogService = Substitute.For<IDialogService>();

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

    [TearDown]
    public void TearDown()
    {
        if (_previousServiceProvider is not null)
        {
            ServiceLocator.Initialize(_previousServiceProvider);
        }
        else
        {
            ServiceLocator.Reset();
        }
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

    [Test]
    public void Load_ShowsTheFolderTheProjectNames()
    {
        var viewModel = CreateViewModel(downloadsFolder: "assets/incoming");

        viewModel.Load();

        viewModel.DownloadsFolderText.Should().Be("assets/incoming");
    }

    [Test]
    public void Load_ShowsAProjectThatNamesNoFolderAsAnEmptyField()
    {
        var viewModel = CreateViewModel();

        viewModel.Load();

        // The placeholder names the default, so the field holds nothing until the project names another.
        viewModel.DownloadsFolderText.Should().BeEmpty();
        viewModel.DefaultDownloadsFolder.Should().Be("downloads");
        viewModel.IsDownloadsFolderInvalid.Should().BeFalse();
    }

    [Test]
    public void EditingTheDownloadsFolder_WritesThePathItNamesIntoTheDraft()
    {
        var viewModel = CreateViewModel();
        viewModel.Load();

        viewModel.DownloadsFolderText = "/assets/incoming/";

        var config = _context.Draft!.ToConfig();
        config.Resources.DownloadsFolder.Should().Be("assets/incoming");
    }

    [TestCase("", Description = "a cleared field")]
    [TestCase("downloads/", Description = "the default, typed")]
    public void TheDefaultFolder_WritesNoKey(string folderText)
    {
        var viewModel = CreateViewModel(downloadsFolder: "assets/incoming");
        viewModel.Load();

        viewModel.DownloadsFolderText = folderText;

        var config = _context.Draft!.ToConfig();
        config.Resources.DownloadsFolder.Should().BeEmpty();
    }

    [Test]
    public void APathThatIsNotAFolderPath_IsReportedAndWritesNoKey()
    {
        var viewModel = CreateViewModel(downloadsFolder: "assets/incoming");
        viewModel.Load();

        viewModel.DownloadsFolderText = "../outside";

        // A load would drop the path, so none is written and downloads go to the default folder. Writing
        // it would make the saved file read back differently, and the section would reload from it.
        viewModel.IsDownloadsFolderInvalid.Should().BeTrue();
        viewModel.DownloadsFolderText.Should().Be("../outside");
        var config = _context.Draft!.ToConfig();
        config.Resources.DownloadsFolder.Should().BeEmpty();
    }

    [Test]
    public void AFolderCelbridgeReserves_IsReportedAndWritesNoKey()
    {
        var viewModel = CreateViewModel(downloadsFolder: "assets/incoming");
        viewModel.Load();

        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        viewModel.DownloadsFolderText = ".git";

        // The message beneath the field distinguishes a reserved folder from a path that is not a folder
        // path, so it is read again whenever the text changes.
        viewModel.IsDownloadsFolderInvalid.Should().BeTrue();
        changedProperties.Should().Contain(nameof(ResourcesSectionViewModel.InvalidDownloadsFolderText));
        var config = _context.Draft!.ToConfig();
        config.Resources.DownloadsFolder.Should().BeEmpty();
    }

    [Test]
    public async Task PickingAFolder_FillsTheField()
    {
        StubPickedFolder(Result<ResourceKey>.Ok(new ResourceKey("assets/incoming")));
        var viewModel = CreateViewModel();
        viewModel.Load();

        await viewModel.PickDownloadsFolderAsync();

        viewModel.DownloadsFolderText.Should().Be("assets/incoming");
        var config = _context.Draft!.ToConfig();
        config.Resources.DownloadsFolder.Should().Be("assets/incoming");
    }

    [Test]
    public async Task PickingTheDefaultFolder_EmptiesTheField()
    {
        StubPickedFolder(Result<ResourceKey>.Ok(new ResourceKey("downloads")));
        var viewModel = CreateViewModel(downloadsFolder: "assets/incoming");
        viewModel.Load();

        await viewModel.PickDownloadsFolderAsync();

        viewModel.DownloadsFolderText.Should().BeEmpty();
    }

    [Test]
    public async Task DismissingThePicker_KeepsTheFolder()
    {
        StubPickedFolder(Result<ResourceKey>.Fail("Resource picker was cancelled"));
        var viewModel = CreateViewModel(downloadsFolder: "assets/incoming");
        viewModel.Load();

        await viewModel.PickDownloadsFolderAsync();

        viewModel.DownloadsFolderText.Should().Be("assets/incoming");
    }

    private void StubPickedFolder(Result<ResourceKey> pickResult)
    {
        _dialogService.ShowFolderPickerDialogAsync(Arg.Any<string?>())
            .Returns(Task.FromResult(pickResult));
    }

    private ResourcesSectionViewModel CreateViewModel(
        string[]? hide = null,
        string[]? searchExclude = null,
        string downloadsFolder = "")
    {
        var config = new ProjectConfig
        {
            Resources = new ResourcesSection
            {
                Hide = hide ?? Array.Empty<string>(),
                SearchExclude = searchExclude ?? Array.Empty<string>(),
                DownloadsFolder = downloadsFolder
            }
        };

        var project = Substitute.For<IProject>();
        project.Config.Returns(config);
        project.ProjectFolderPath.Returns(ProjectFolderPath);
        _projectService.CurrentProject.Returns(project);

        _context.Draft = new ProjectConfigDraft(config);

        return new ResourcesSectionViewModel(_context, _dialogService);
    }
}
