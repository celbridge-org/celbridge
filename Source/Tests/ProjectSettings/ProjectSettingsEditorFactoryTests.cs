using Celbridge.Documents;
using Celbridge.Documents.Services;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.ProjectSettings;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.ProjectSettings;

/// <summary>
/// Covers the claim the Project Settings editor makes on .celbridge files. A folder may hold several
/// configurations, and only the loaded one has a reconciliation behind it, so the rest resolve to the
/// Code Editor and are hand-edited as TOML.
/// </summary>
[TestFixture]
public class ProjectSettingsEditorFactoryTests
{
    private static string ProjectFolderPath =>
        OperatingSystem.IsWindows() ? @"C:\Projects\Acme" : "/projects/acme";

    private IProjectService _projectService = null!;
    private ProjectSettingsEditorFactory _factory = null!;

    [SetUp]
    public void Setup()
    {
        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer[Arg.Any<string>()].Returns(callInfo =>
        {
            var name = (string)callInfo[0];
            return new LocalizedString(name, name);
        });

        _projectService = Substitute.For<IProjectService>();
        StubCurrentProject("Acme.celbridge");

        _factory = new ProjectSettingsEditorFactory(stringLocalizer, _projectService);
    }

    [Test]
    public void CanHandleResource_TheLoadedProjectFile_IsClaimed()
    {
        _factory.CanHandleResource(new ResourceKey("Acme.celbridge")).Should().BeTrue();
    }

    [Test]
    public void CanHandleResource_AnotherConfigurationInTheSameFolder_IsNotClaimed()
    {
        _factory.CanHandleResource(new ResourceKey("Designer.celbridge")).Should().BeFalse();
    }

    [Test]
    public void CanHandleResource_WithNoProjectLoaded_IsNotClaimed()
    {
        _projectService.CurrentProject.Returns((IProject?)null);

        _factory.CanHandleResource(new ResourceKey("Acme.celbridge")).Should().BeFalse();
    }

    [Test]
    public void PickList_ForTheLoadedProjectFile_OffersBothEditors()
    {
        var registry = CreateRegistry();

        var candidates = registry.GetUserPickableFactoriesForResource(new ResourceKey("Acme.celbridge"));

        candidates.Select(candidate => candidate.EditorId).Should().Equal(
            BuiltInEditors.ProjectSettingsEditorId,
            BuiltInEditors.CodeEditorId);
    }

    [Test]
    public void PickList_ForAnotherConfiguration_HoldsOnlyTheCodeEditor()
    {
        // Fewer than two entries means no "Open with..." choice is offered, which is what makes the
        // alternate configuration open as text without a dialog.
        var registry = CreateRegistry();

        var candidates = registry.GetUserPickableFactoriesForResource(new ResourceKey("Designer.celbridge"));

        candidates.Select(candidate => candidate.EditorId).Should().Equal(BuiltInEditors.CodeEditorId);
    }

    private DocumentEditorRegistry CreateRegistry()
    {
        var textBinarySniffer = Substitute.For<ITextBinarySniffer>();
        var registry = new DocumentEditorRegistry(textBinarySniffer);

        registry.RegisterFactory(_factory);
        registry.RegisterFactory(CreateCodeEditorFactory());

        return registry;
    }

    private static IDocumentEditorFactory CreateCodeEditorFactory()
    {
        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.EditorId.Returns(BuiltInEditors.CodeEditorId);
        factory.DisplayName.Returns("Code Editor");
        factory.SupportedExtensions.Returns(new List<string> { ".txt" });
        factory.CanHandleResource(Arg.Any<ResourceKey>()).Returns(true);

        return factory;
    }

    // A real Project rather than a substitute, so the factory runs the actual IsProjectFile comparison
    // instead of a stub's default answer.
    private void StubCurrentProject(string projectFileName)
    {
        var project = new Project(
            Path.Combine(ProjectFolderPath, projectFileName),
            Path.GetFileNameWithoutExtension(projectFileName),
            ProjectFolderPath,
            new ProjectConfig(),
            MigrationResult.Success(),
            ConfigIsHealthy: true,
            ConfigLoadFailure: null);

        _projectService.CurrentProject.Returns(project);
    }
}
