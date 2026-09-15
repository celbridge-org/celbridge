using Celbridge.Commands;
using Celbridge.Packages;
using Celbridge.ProjectSettings.ViewModels;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Resources;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.ProjectSettings;

/// <summary>
/// Covers which package rows in the Packages section show a version beside the package name.
/// </summary>
[TestFixture]
public class PackagesSectionViewModelTests
{
    private IServiceProvider? _previousServiceProvider;
    private IPackageService _packageService = null!;
    private ProjectSettingsContext _context = null!;

    [SetUp]
    public void Setup()
    {
        // The section acquires its logger from the global ServiceLocator, and labels its rows through the
        // localizer, so both are registered for the duration of the fixture.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(typeof(ILogger<>), typeof(Celbridge.Logging.Services.Logger<>));
        services.AddSingleton(Substitute.For<IStringLocalizer>());

        _previousServiceProvider = ServiceLocator.ServiceProvider;
        ServiceLocator.Initialize(services.BuildServiceProvider());

        _packageService = Substitute.For<IPackageService>();
        _packageService.GetContributionIssues().Returns([]);

        var resourceRegistry = Substitute.For<IResourceRegistry>();
        resourceRegistry.GetResourceKey(Arg.Any<string>())
            .Returns(Result<ResourceKey>.Ok(new ResourceKey("packages/acme-widget/package.toml")));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.PackageService.Returns(_packageService);
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _context = new ProjectSettingsContext(
            workspaceWrapper,
            Substitute.For<IProjectService>(),
            Substitute.For<ICommandService>(),
            () => { });
        _context.Draft = new ProjectConfigDraft(new ProjectConfig());
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
    public void Load_ProjectPackage_ShowsItsPackageVersion()
    {
        var package = CreatePackage("acme-widget", PackageOrigin.Project, new SemanticVersion(2, 1, 0));
        _packageService.GetAllPackages().Returns([package]);

        var viewModel = CreateViewModel();
        viewModel.Load();

        var row = viewModel.Packages.Should().ContainSingle().Subject;
        row.HasVersion.Should().BeTrue();
    }

    [Test]
    public void Load_ProjectPackageAtTheDefaultVersion_ShowsNoVersion()
    {
        // A manifest that sets no version loads at the default, so showing it would tell the user nothing.
        var package = CreatePackage("acme-widget", PackageOrigin.Project, SemanticVersion.Default);
        _packageService.GetAllPackages().Returns([package]);

        var viewModel = CreateViewModel();
        viewModel.Load();

        var row = viewModel.Packages.Should().ContainSingle().Subject;
        row.HasVersion.Should().BeFalse();
        row.VersionText.Should().BeEmpty();
    }

    [Test]
    public void Load_BundledPackage_ShowsNoVersion()
    {
        var package = CreatePackage("celbridge-acme", PackageOrigin.Bundled, new SemanticVersion(2, 1, 0));
        _packageService.GetAllPackages().Returns([package]);

        var viewModel = CreateViewModel();
        viewModel.Load();

        var row = viewModel.Packages.Should().ContainSingle().Subject;
        row.HasVersion.Should().BeFalse();
        row.VersionText.Should().BeEmpty();
    }

    private PackagesSectionViewModel CreateViewModel()
    {
        return new PackagesSectionViewModel(_context, Substitute.For<IPackageLocalizationService>());
    }

    private static Package CreatePackage(string name, PackageOrigin origin, SemanticVersion packageVersion)
    {
        return new Package
        {
            Info = new PackageInfo
            {
                Name = name,
                Origin = origin,
                PackageFolder = Path.Combine(Path.GetTempPath(), name),
                PackageVersion = packageVersion
            }
        };
    }
}
