using Celbridge.Commands;
using Celbridge.ProjectSettings.ViewModels;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.ProjectSettings;

/// <summary>
/// Covers the project version box in the Information section: the default it shows while empty, the text it
/// flags as invalid, and what it writes back into the config draft.
/// </summary>
[TestFixture]
public class InformationSectionViewModelTests
{
    private ProjectSettingsContext _context = null!;
    private bool _edited;

    [SetUp]
    public void Setup()
    {
        _edited = false;

        _context = new ProjectSettingsContext(
            Substitute.For<IWorkspaceWrapper>(),
            Substitute.For<IProjectService>(),
            Substitute.For<ICommandService>(),
            () => _edited = true);
    }

    [Test]
    public void Load_ProjectWithNoVersion_ShowsTheDefaultAsThePlaceholder()
    {
        var viewModel = CreateViewModel(projectVersion: null);

        viewModel.Load();

        viewModel.ProjectVersionText.Should().BeEmpty();
        viewModel.ProjectVersionPlaceholder.Should().Be("1.0.0");
        viewModel.IsProjectVersionInvalid.Should().BeFalse("an empty box is the default version");
        _edited.Should().BeFalse();
    }

    [Test]
    public void EditingTheProjectVersion_ToAValidVersion_WritesIt()
    {
        var viewModel = CreateViewModel(projectVersion: "1.0.0");
        viewModel.Load();

        viewModel.ProjectVersionText = "1.2.0";

        viewModel.IsProjectVersionInvalid.Should().BeFalse();
        _edited.Should().BeTrue();
        _context.Draft!.ToConfig().Celbridge.ProjectVersion.Should().Be("1.2.0");
    }

    [Test]
    public void EditingTheProjectVersion_ToAMalformedVersion_IsInvalidAndWritesNothing()
    {
        var viewModel = CreateViewModel(projectVersion: "1.0.0");
        viewModel.Load();

        viewModel.ProjectVersionText = "1.2";

        viewModel.IsProjectVersionInvalid.Should().BeTrue();
        _edited.Should().BeFalse();
        _context.Draft!.ToConfig().Celbridge.ProjectVersion.Should().Be("1.0.0");
    }

    [Test]
    public void ClearingTheProjectVersion_RemovesTheKey()
    {
        var viewModel = CreateViewModel(projectVersion: "1.2.0");
        viewModel.Load();

        viewModel.ProjectVersionText = string.Empty;

        viewModel.IsProjectVersionInvalid.Should().BeFalse();
        _context.Draft!.Serialize().Should().NotContain("project-version");
    }

    private InformationSectionViewModel CreateViewModel(string? projectVersion)
    {
        var config = new ProjectConfig
        {
            Celbridge = new CelbridgeSection
            {
                CelbridgeVersion = "1.0.0",
                ProjectVersion = projectVersion
            }
        };

        _context.Draft = new ProjectConfigDraft(config);

        return new InformationSectionViewModel(_context);
    }
}
