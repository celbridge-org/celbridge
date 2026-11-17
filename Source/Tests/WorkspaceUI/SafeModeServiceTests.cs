using Celbridge.Settings;
using Celbridge.Dependencies;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Services;

namespace Celbridge.Tests.WorkspaceUI;

/// <summary>
/// Covers the Safe Mode state: each of the project's causes, two causes at once, the state staying fixed, and the
/// allow list.
/// </summary>
[TestFixture]
public class SafeModeServiceTests
{
    private IProjectService _projectService = null!;

    [SetUp]
    public void Setup()
    {
        _projectService = Substitute.For<IProjectService>();
    }

    [Test]
    public void AProjectThatLoadedCleanly_IsNotInSafeMode()
    {
        SetCurrentProject(isTrusted: true, configIsHealthy: true);

        var safeModeService = new SafeModeService(_projectService);

        safeModeService.IsActive.Should().BeFalse();
        safeModeService.Causes.Should().BeEmpty();
    }

    [Test]
    public void AProjectFileThatFailedToLoad_OpensSafeMode()
    {
        SetCurrentProject(isTrusted: true, configIsHealthy: false);

        var safeModeService = new SafeModeService(_projectService);

        safeModeService.IsActive.Should().BeTrue();
        safeModeService.Causes.Should().Equal(SafeModeCause.ConfigurationFailed);
    }

    [Test]
    public void AProjectWhoseDependencyFailedToInstall_OpensSafeMode()
    {
        var project = SetCurrentProject(isTrusted: true, configIsHealthy: true);
        var failure = new DependencyInstallFailure(FeatureFlagCatalog.Python, "uv", Result.Fail("The network is down"));
        project.DependencyInstallFailure.Returns(failure);

        var safeModeService = new SafeModeService(_projectService);

        safeModeService.IsActive.Should().BeTrue();
        safeModeService.Causes.Should().Equal(SafeModeCause.DependencyInstallFailed);
    }

    [Test]
    public void AnUntrustedProject_OpensSafeMode()
    {
        SetCurrentProject(isTrusted: false, configIsHealthy: true);

        var safeModeService = new SafeModeService(_projectService);

        safeModeService.IsActive.Should().BeTrue();
        safeModeService.Causes.Should().Equal(SafeModeCause.Untrusted);
    }

    [Test]
    public void AnUntrustedProjectWhoseFileFailedToLoad_KeepsBothCauses()
    {
        SetCurrentProject(isTrusted: false, configIsHealthy: false);

        var safeModeService = new SafeModeService(_projectService);

        safeModeService.IsActive.Should().BeTrue();
        safeModeService.Causes.Should().Equal(SafeModeCause.Untrusted, SafeModeCause.ConfigurationFailed);
    }

    [Test]
    public void TheState_StaysFixedUntilTheWorkspaceIsCreatedAgain()
    {
        SetCurrentProject(isTrusted: true, configIsHealthy: false);
        var safeModeService = new SafeModeService(_projectService);

        // A reload creates a new workspace, and so a new service.
        SetCurrentProject(isTrusted: true, configIsHealthy: true);

        safeModeService.IsActive.Should().BeTrue();
        safeModeService.Causes.Should().Equal(SafeModeCause.ConfigurationFailed);
    }

    [Test]
    public void OutsideSafeMode_EveryIdIsAllowed()
    {
        SetCurrentProject(isTrusted: true, configIsHealthy: true);

        var safeModeService = new SafeModeService(_projectService);

        safeModeService.IsAllowed(BuiltInEditors.MarkdownEditorId).Should().BeTrue();
        safeModeService.IsAllowed(BuiltInShortcutIds.Community).Should().BeTrue();
        safeModeService.IsAllowed(new EditorId("acme-tools.widget")).Should().BeTrue();
    }

    [Test]
    public void InSafeMode_TheIdsOnTheListAreAllowed()
    {
        SetCurrentProject(isTrusted: true, configIsHealthy: false);

        var safeModeService = new SafeModeService(_projectService);

        safeModeService.IsAllowed(BuiltInEditors.CodeEditorId).Should().BeTrue();
        safeModeService.IsAllowed(BuiltInEditors.ReportViewerId).Should().BeTrue();
        safeModeService.IsAllowed(BuiltInEditors.ProjectSettingsEditorId).Should().BeTrue();
        safeModeService.IsAllowed(BuiltInShortcutIds.ProjectSettings).Should().BeTrue();
        safeModeService.IsAllowed(BuiltInUtilityIds.Explorer).Should().BeTrue();
        safeModeService.IsAllowed(BuiltInUtilityIds.Search).Should().BeTrue();
    }

    [Test]
    public void InSafeMode_TheEditorsThatAreOffAreNotAllowed()
    {
        SetCurrentProject(isTrusted: true, configIsHealthy: false);

        var safeModeService = new SafeModeService(_projectService);

        safeModeService.IsAllowed(BuiltInEditors.MarkdownEditorId).Should().BeFalse();
        safeModeService.IsAllowed(BuiltInEditors.HtmlEditorId).Should().BeFalse();
        safeModeService.IsAllowed(BuiltInEditors.BrowserEditorId).Should().BeFalse();
        safeModeService.IsAllowed(BuiltInEditors.SpreadsheetEditorId).Should().BeFalse();
        safeModeService.IsAllowed(BuiltInEditors.FileViewerId).Should().BeFalse();
    }

    [Test]
    public void InSafeMode_ThePackagesAndRailButtonsThatAreOffAreNotAllowed()
    {
        SetCurrentProject(isTrusted: true, configIsHealthy: false);

        var safeModeService = new SafeModeService(_projectService);

        // A project's own package, then the bundled console, Notes and Utility Demo packages.
        safeModeService.IsAllowed(new EditorId("acme-tools.widget")).Should().BeFalse();
        safeModeService.IsAllowed(new EditorId("celbridge-console.console")).Should().BeFalse();
        safeModeService.IsAllowed(new EditorId("celbridge-notes.note")).Should().BeFalse();
        safeModeService.IsAllowed(new EditorId("celbridge-utility-demo.utility-demo")).Should().BeFalse();

        // The Community button, and the rail button of the project's first shortcut.
        safeModeService.IsAllowed(BuiltInShortcutIds.Community).Should().BeFalse();
        safeModeService.IsAllowed(new EditorId("celbridge.shortcut-1")).Should().BeFalse();
    }

    private IProject SetCurrentProject(bool isTrusted, bool configIsHealthy)
    {
        var project = Substitute.For<IProject>();
        project.IsTrusted.Returns(isTrusted);
        project.ConfigIsHealthy.Returns(configIsHealthy);

        _projectService.CurrentProject.Returns(project);

        return project;
    }
}
