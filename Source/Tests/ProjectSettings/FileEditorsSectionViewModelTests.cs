using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Packages;
using Celbridge.ProjectSettings.ViewModels;
using Celbridge.Projects;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.ProjectSettings;

/// <summary>
/// Covers which file types the File Editors section lists: the ones that offer the user a choice, plus
/// the ones carrying an association that no editor answers to.
/// </summary>
[TestFixture]
public class FileEditorsSectionViewModelTests
{
    private const string CodeEditorId = "celbridge.code";
    private const string MarkdownEditorId = "celbridge.markdown";
    private const string RemovedEditorId = "acme.removed";

    private IServiceProvider? _previousServiceProvider;
    private IPackageService _packageService = null!;
    private IDocumentsService _documentsService = null!;
    private IProjectService _projectService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        // The section acquires its logger from the global ServiceLocator, and names an unavailable
        // editor through the localizer, so both are registered for the duration of the fixture.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(typeof(ILogger<>), typeof(Celbridge.Logging.Services.Logger<>));
        services.AddSingleton(Substitute.For<IStringLocalizer>());

        _previousServiceProvider = ServiceLocator.ServiceProvider;
        ServiceLocator.Initialize(services.BuildServiceProvider());

        _packageService = Substitute.For<IPackageService>();
        _packageService.GetResolvedEditors().Returns([]);
        _packageService.GetBuiltInEditors().Returns([]);

        _documentsService = Substitute.For<IDocumentsService>();
        _documentsService.IsReservedFileType(Arg.Any<string>()).Returns(false);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.PackageService.Returns(_packageService);
        workspaceService.DocumentsService.Returns(_documentsService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _projectService = Substitute.For<IProjectService>();
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
    public void Load_ExtensionWithOneEditor_IsNotListed()
    {
        // A file type only one editor opens presents no choice, so listing it would be noise.
        SetClaimedExtensions(".png");
        SetCandidates(".png", CodeEditorId);

        var viewModel = CreateViewModel();
        viewModel.Load();

        viewModel.FileTypeRows.Should().BeEmpty();
        viewModel.ContentState.Should().Be(SectionContentState.Empty);
    }

    [Test]
    public void Load_ExtensionWithTwoEditors_IsListedWithBothCandidates()
    {
        SetClaimedExtensions(".md");
        SetCandidates(".md", MarkdownEditorId, CodeEditorId);

        var viewModel = CreateViewModel();
        viewModel.Load();

        var row = viewModel.FileTypeRows.Should().ContainSingle().Subject;
        row.Extension.Should().Be(".md");
        row.Candidates.Select(candidate => candidate.EditorId)
            .Should().BeEquivalentTo([MarkdownEditorId, CodeEditorId]);
        viewModel.ContentState.Should().Be(SectionContentState.Populated);
    }

    [Test]
    public void Load_ReservedExtension_IsNotListed()
    {
        SetClaimedExtensions(".celbridge");
        SetCandidates(".celbridge", "celbridge.project-settings", CodeEditorId);
        _documentsService.IsReservedFileType(".celbridge").Returns(true);

        var viewModel = CreateViewModel();
        viewModel.Load();

        viewModel.FileTypeRows.Should().BeEmpty();
    }

    [Test]
    public void Load_UtilityContribution_ContributesNoFileTypes()
    {
        _packageService.GetResolvedEditors().Returns([CreateResolvedEditor(isUtility: true, "._utility")]);
        SetCandidates("._utility", CodeEditorId, "acme.utility");

        var viewModel = CreateViewModel();
        viewModel.Load();

        viewModel.FileTypeRows.Should().BeEmpty();
    }

    [Test]
    public void Load_AssociationNamingAnEditorThatNoLongerClaimsTheType_IsListedSoItCanBeCleared()
    {
        // Deactivating the package that owned the pinned editor leaves an association nothing answers
        // to. Without a row the user has no way to reach it.
        SetClaimedExtensions(".widget");
        SetCandidates(".widget", CodeEditorId);

        var viewModel = CreateViewModel(associations: new Dictionary<string, string>
        {
            [".widget"] = RemovedEditorId
        });
        viewModel.Load();

        var row = viewModel.FileTypeRows.Should().ContainSingle().Subject;
        row.Candidates.Select(candidate => candidate.EditorId)
            .Should().BeEquivalentTo([CodeEditorId, RemovedEditorId]);
        row.SelectedCandidate!.EditorId.Should().Be(RemovedEditorId);
    }

    [Test]
    public void Load_WithoutAWorkspace_ReportsNotLoadedRatherThanEmpty()
    {
        _workspaceWrapper.WorkspaceService.Returns((IWorkspaceService?)null);

        var viewModel = CreateViewModel();
        viewModel.Load();

        viewModel.FileTypeRows.Should().BeEmpty();
        viewModel.ContentState.Should().Be(SectionContentState.NotLoaded);
    }

    private FileEditorsSectionViewModel CreateViewModel(IReadOnlyDictionary<string, string>? associations = null)
    {
        var config = new ProjectConfig
        {
            Celbridge = new CelbridgeSection
            {
                EditorAssociations = associations ?? new Dictionary<string, string>()
            }
        };

        var project = Substitute.For<IProject>();
        project.Config.Returns(config);
        _projectService.CurrentProject.Returns(project);

        var context = new ProjectSettingsContext(
            _workspaceWrapper,
            _projectService,
            Substitute.For<ICommandService>(),
            () => { });

        return new FileEditorsSectionViewModel(context, Substitute.For<IFileTypeCatalog>());
    }

    private void SetClaimedExtensions(params string[] extensions)
    {
        _packageService.GetBuiltInEditors().Returns([CreateResolvedEditor(isUtility: false, extensions)]);
    }

    // The candidates the resolver reports for an extension, in resolution order, so the first is the
    // editor that opens the file by default.
    private void SetCandidates(string extension, params string[] editorIds)
    {
        var candidates = editorIds
            .Select(editorId => new EditorCandidate(new EditorId(editorId), editorId))
            .ToList();

        var pick = new ExtensionEditorCandidates(candidates, new EditorId(editorIds[0]));
        _documentsService.GetEditorCandidatesForExtension(extension).Returns(pick);
    }

    private static ResolvedEditor CreateResolvedEditor(bool isUtility, params string[] extensions)
    {
        var fileTypes = extensions
            .Select(extension => new EditorFileType { FileExtension = extension })
            .ToList();

        UtilityDescriptor? utilityDescriptor = null;
        if (isUtility)
        {
            utilityDescriptor = new UtilityDescriptor { ResourceExtension = extensions[0] };
        }

        var contribution = new EditorContribution
        {
            Id = "editor",
            FileTypes = fileTypes,
            UtilityDescriptor = utilityDescriptor
        };

        return new ResolvedEditor
        {
            EditorId = new EditorId("acme.editor"),
            Contribution = contribution
        };
    }
}
