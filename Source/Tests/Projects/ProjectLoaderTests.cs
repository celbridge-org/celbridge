using Celbridge.Dialog;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Settings;
using Celbridge.Tests.Localization;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Covers how ProjectLoader refuses a project this version of Celbridge cannot open. The alert names the
/// versions involved, and the project is never loaded.
/// </summary>
[TestFixture]
public class ProjectLoaderTests
{
    private const string ProjectFilePath = "/projects/Acme/Acme.celbridge";

    private IProjectMigrationService _migrationService = null!;
    private IProjectService _projectService = null!;
    private IDialogService _dialogService = null!;
    private ProjectLoader _projectLoader = null!;

    [SetUp]
    public void Setup()
    {
        _migrationService = Substitute.For<IProjectMigrationService>();
        _projectService = Substitute.For<IProjectService>();
        _dialogService = Substitute.For<IDialogService>();

        // Backed by the application's own resources, so the assertions read the text a user sees.
        var localizerService = new TestLocalizerService();
        var stringLocalizer = Substitute.For<IStringLocalizer>();

        stringLocalizer[Arg.Any<string>()].Returns(callInfo =>
        {
            var name = (string)callInfo[0];

            return new LocalizedString(name, localizerService.GetString(name));
        });

        stringLocalizer[Arg.Any<string>(), Arg.Any<object[]>()].Returns(callInfo =>
        {
            var name = (string)callInfo[0];
            var arguments = (object[])callInfo[1];

            return new LocalizedString(name, localizerService.GetString(name, arguments));
        });

        _projectLoader = new ProjectLoader(
            Substitute.For<ILogger<ProjectLoader>>(),
            _migrationService,
            _projectService,
            _dialogService,
            Substitute.For<IApplicationShell>(),
            Substitute.For<ISettingsService>(),
            Substitute.For<IWorkspaceWrapper>(),
            Substitute.For<IMessengerService>(),
            stringLocalizer,
            Substitute.For<IProjectLoadReporter>());
    }

    [Test]
    public async Task LoadProjectAsync_NewerCelbridgeVersion_AlertNamesTheVersionToInstall()
    {
        var migrationResult = MigrationResult.WithVersions(
            MigrationStatus.NewerCelbridgeVersion,
            Result.Fail("The project is from a newer version"),
            "1.4.0",
            "1.3.0");
        _migrationService.CheckMigrationAsync(ProjectFilePath).Returns(migrationResult);

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath);

        result.IsFailure.Should().BeTrue();

        await _dialogService.Received(1).ShowAlertDialogAsync(
            "Newer Version Required",
            Arg.Is<string>(message =>
                message.Contains("\"Acme\"") &&
                message.Contains("v1.4.0 or later") &&
                message.Contains("(v1.3.0)")));

        await _projectService.DidNotReceiveWithAnyArgs().LoadProjectAsync(default!, default!);
    }

    [Test]
    public async Task LoadProjectAsync_UnsupportedCelbridgeVersion_AlertNamesTheOldestSupportedVersion()
    {
        var migrationResult = MigrationResult.WithVersions(
            MigrationStatus.UnsupportedCelbridgeVersion,
            Result.Fail("The project is from an unsupported version"),
            "0.3.0",
            "1.3.0");
        _migrationService.CheckMigrationAsync(ProjectFilePath).Returns(migrationResult);

        var result = await _projectLoader.LoadProjectAsync(ProjectFilePath);

        result.IsFailure.Should().BeTrue();

        await _dialogService.Received(1).ShowAlertDialogAsync(
            "Project Not Supported",
            Arg.Is<string>(message =>
                message.Contains("\"Acme\"") &&
                message.Contains("v0.3.0") &&
                message.Contains($"before v{ProjectConstants.MinimumSupportedCelbridgeVersion}")));

        await _projectService.DidNotReceiveWithAnyArgs().LoadProjectAsync(default!, default!);
    }
}
