using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.ProjectSettings.ViewModels;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Utilities;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.ProjectSettings;

/// <summary>
/// Covers the Resources section: what the two pattern blocks, the downloads folder and the package folders
/// write back into the config draft, which lines are reported as written in a dialect the matcher does not
/// read, and which downloads folders and package folders are reported as unusable.
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
            explorerExclude: new[] { ".gitignore" },
            searchExclude: new[] { "node_modules", "bin" });

        viewModel.Load();

        viewModel.ExplorerExcludePatternsText.Should().Be(".gitignore");
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

        var viewModel = CreateViewModel(explorerExclude: new[] { ".gitignore" });
        viewModel.Load();

        edited.Should().BeFalse();
    }

    [Test]
    public void EditingABlock_WritesEveryLineIntoTheDraft()
    {
        var viewModel = CreateViewModel(explorerExclude: new[] { ".gitignore" });
        viewModel.Load();

        viewModel.ExplorerExcludePatternsText = ".gitignore\ndrafts/**";

        var config = _context.Draft!.ToConfig();
        config.Explorer.Exclude.Should().Equal(".gitignore", "drafts/**");
    }

    [Test]
    public void EditingABlock_SplitsTheLineEndingTheTextBoxUses()
    {
        // A WinUI text box reports its line breaks as bare carriage returns, so a block split on line
        // feeds alone arrives as one unusable pattern.
        var viewModel = CreateViewModel();
        viewModel.Load();

        viewModel.ExplorerExcludePatternsText = ".claude\r.gitignore";

        var config = _context.Draft!.ToConfig();
        config.Explorer.Exclude.Should().Equal(".claude", ".gitignore");
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
        config.Search.Exclude.Should().Equal("node_modules", "bin");
    }

    [Test]
    public void ClearingABlock_EmptiesTheList()
    {
        var viewModel = CreateViewModel(searchExclude: new[] { "node_modules" });
        viewModel.Load();

        viewModel.SearchExcludePatternsText = string.Empty;

        var config = _context.Draft!.ToConfig();
        config.Search.Exclude.Should().BeEmpty();
    }

    [Test]
    public void GitignoreSyntax_IsAccepted()
    {
        var viewModel = CreateViewModel();
        viewModel.Load();

        viewModel.ExplorerExcludePatternsText.Should().BeEmpty();
        viewModel.HasInvalidExplorerExcludePattern.Should().BeFalse("an empty block is unfinished, not wrong");

        // A leading slash, a '!', a comment and gitignore's wildcards are all valid.
        viewModel.ExplorerExcludePatternsText = "/rooted.txt\nsrc/**\n!keep.txt\n# Build output\nbin/\nlog?.txt\n*.[oa]";
        viewModel.HasInvalidExplorerExcludePattern.Should().BeFalse();
    }

    [TestCase(@"src\bin", Description = "a Windows separator")]
    [TestCase(@"notes\", Description = "a trailing backslash")]
    [TestCase("data[12.csv", Description = "an unclosed bracket")]
    [TestCase("*.{js,ts}", Description = "braces")]
    [TestCase("[[:letter:]]*", Description = "an unknown named class")]
    public void RefusedLine_IsReportedForTheBlockHoldingIt(string line)
    {
        var viewModel = CreateViewModel();
        viewModel.Load();

        viewModel.ExplorerExcludePatternsText = "src/**\n" + line;

        viewModel.HasInvalidExplorerExcludePattern.Should().BeTrue();
        viewModel.HasInvalidSearchExcludePattern.Should().BeFalse("the other block is judged on its own lines");
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
        config.Downloads.Folder.Should().Be("assets/incoming");
    }

    [TestCase("", Description = "a cleared field")]
    [TestCase("downloads/", Description = "the default, typed")]
    public void TheDefaultFolder_WritesNoKey(string folderText)
    {
        var viewModel = CreateViewModel(downloadsFolder: "assets/incoming");
        viewModel.Load();

        viewModel.DownloadsFolderText = folderText;

        var config = _context.Draft!.ToConfig();
        config.Downloads.Folder.Should().BeEmpty();
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
        config.Downloads.Folder.Should().BeEmpty();
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
        config.Downloads.Folder.Should().BeEmpty();
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
        config.Downloads.Folder.Should().Be("assets/incoming");
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

    [Test]
    public void Load_ListsThePackageFoldersAfterTheFixedOne()
    {
        var viewModel = CreateViewModel(packageFolders: new[] { "vendor/packages", "lib" });

        viewModel.Load();

        viewModel.DefaultPackageFolder.Should().Be("packages");
        viewModel.PackageFolders.Select(packageFolder => packageFolder.Path).Should().Equal("vendor/packages", "lib");
        viewModel.PackageFolders.Should().OnlyContain(packageFolder => !packageFolder.HasProblem);
    }

    [Test]
    public void AddingAndRemovingPackageFolders_WritesTheListIntoTheDraft()
    {
        var viewModel = CreateViewModel(packageFolders: new[] { "lib" });
        viewModel.Load();

        viewModel.AddPackageFolder();
        viewModel.PackageFolders[1].Path = "/vendor/packages/";

        _context.Draft!.ToConfig().Packages.Folders.Should().Equal("lib", "vendor/packages");

        viewModel.RemovePackageFolder(viewModel.PackageFolders[0]);

        _context.Draft!.ToConfig().Packages.Folders.Should().Equal("vendor/packages");
    }

    [TestCase("/", PackageFolderProblem.ProjectRoot)]
    [TestCase("../outside", PackageFolderProblem.InvalidPath)]
    [TestCase(".git/hooks", PackageFolderProblem.Reserved)]
    [TestCase("packages", PackageFolderProblem.AlreadyListed)]
    [TestCase("lib/", PackageFolderProblem.AlreadyListed)]
    public void AnUnusablePackageFolder_IsReportedAndNotWritten(string path, PackageFolderProblem expectedProblem)
    {
        var viewModel = CreateViewModel(packageFolders: new[] { "lib" });
        viewModel.Load();

        viewModel.AddPackageFolder();
        var packageFolder = viewModel.PackageFolders[1];

        packageFolder.HasProblem.Should().BeFalse("a blank row is unfinished, not wrong");

        packageFolder.Path = path;

        packageFolder.Problem.Should().Be(expectedProblem);
        packageFolder.HasProblem.Should().BeTrue();
        _context.Draft!.ToConfig().Packages.Folders.Should().Equal("lib");
    }

    [Test]
    public async Task PickingAPackageFolder_FillsTheRow()
    {
        StubPickedFolder(Result<ResourceKey>.Ok(new ResourceKey("vendor/packages")));
        var viewModel = CreateViewModel();
        viewModel.Load();
        viewModel.AddPackageFolder();

        await viewModel.PickPackageFolderAsync(viewModel.PackageFolders[0]);

        viewModel.PackageFolders[0].Path.Should().Be("vendor/packages");
        _context.Draft!.ToConfig().Packages.Folders.Should().Equal("vendor/packages");
    }

    private void StubPickedFolder(Result<ResourceKey> pickResult)
    {
        _dialogService.ShowFolderPickerDialogAsync(Arg.Any<string?>())
            .Returns(Task.FromResult(pickResult));
    }

    private ResourcesSectionViewModel CreateViewModel(
        string[]? explorerExclude = null,
        string[]? searchExclude = null,
        string downloadsFolder = "",
        string[]? packageFolders = null)
    {
        var config = new ProjectConfig
        {
            Explorer = new ExplorerSection
            {
                Exclude = explorerExclude ?? Array.Empty<string>()
            },
            Search = new SearchSection
            {
                Exclude = searchExclude ?? Array.Empty<string>()
            },
            Downloads = new DownloadsSection
            {
                Folder = downloadsFolder
            },
            Packages = new PackagesSection
            {
                Folders = packageFolders ?? Array.Empty<string>()
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
