using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Documents;
using Celbridge.Explorer.Menu;
using Celbridge.Explorer.Menu.Options;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Explorer;

/// <summary>
/// Unit tests for OpenCelMenuOption visibility logic. The menu is the only
/// power-user surface that exposes .cel sidecars in the UI; it is gated by
/// the [features].open-cel flag and by the presence of a sidecar on the
/// clicked resource.
/// </summary>
[TestFixture]
public class OpenCelMenuOptionTests
{
    private IStringLocalizer _stringLocalizer = null!;
    private ICommandService _commandService = null!;
    private IProjectService _projectService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _stringLocalizer = Substitute.For<IStringLocalizer>();
        _commandService = Substitute.For<ICommandService>();
        _projectService = Substitute.For<IProjectService>();
        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
    }

    private OpenCelMenuOption CreateOption()
    {
        return new OpenCelMenuOption(
            _stringLocalizer,
            _commandService,
            _projectService,
            _workspaceWrapper);
    }

    private static ExplorerMenuContext ContextFor(IResource? clickedResource)
    {
        var projectFolder = Substitute.For<IFolderResource>();
        return new ExplorerMenuContext(
            ClickedResource: clickedResource,
            SelectedResources: clickedResource is null ? Array.Empty<IResource>() : new[] { clickedResource },
            ProjectFolder: projectFolder,
            IsProjectFolderTargeted: false,
            HasClipboardData: false,
            ClipboardContentType: ClipboardContentType.None,
            ClipboardOperation: ClipboardContentOperation.None);
    }

    private void SetFeatureFlag(bool enabled)
    {
        var features = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (enabled)
        {
            features["open-cel"] = true;
        }

        var config = new ProjectConfig
        {
            Features = features
        };

        var project = Substitute.For<IProject>();
        project.Config.Returns(config);
        _projectService.CurrentProject.Returns(project);
    }

    private static IFileResource CreateFileResource(string name, SidecarLink? sidecar)
    {
        var file = Substitute.For<IFileResource>();
        file.Name.Returns(name);
        file.Sidecar.Returns(sidecar);
        return file;
    }

    [Test]
    public void GetState_HiddenWhenFlagDisabled_EvenIfSidecarExists()
    {
        // Default behaviour: most users never want this menu option, so it stays
        // hidden until the project opts in via the feature flag.
        SetFeatureFlag(enabled: false);
        var sidecar = new SidecarLink(new ResourceKey("notes.md.cel"), CelFileStatus.Healthy);
        var clickedFile = CreateFileResource("notes.md", sidecar);

        var state = CreateOption().GetState(ContextFor(clickedFile));

        state.IsVisible.Should().BeFalse();
        state.IsEnabled.Should().BeFalse();
    }

    [Test]
    public void GetState_HiddenWhenSidecarMissing_EvenWithFlagEnabled()
    {
        // The option does nothing useful when no sidecar exists; offering it
        // would invite empty-sidecar creation through a power-user surface,
        // which is not what this affordance is for.
        SetFeatureFlag(enabled: true);
        var clickedFile = CreateFileResource("notes.md", sidecar: null);

        var state = CreateOption().GetState(ContextFor(clickedFile));

        state.IsVisible.Should().BeFalse();
        state.IsEnabled.Should().BeFalse();
    }

    [Test]
    public void GetState_VisibleWhenFlagEnabledAndSidecarPresent()
    {
        SetFeatureFlag(enabled: true);
        var sidecar = new SidecarLink(new ResourceKey("notes.md.cel"), CelFileStatus.Healthy);
        var clickedFile = CreateFileResource("notes.md", sidecar);

        var state = CreateOption().GetState(ContextFor(clickedFile));

        state.IsVisible.Should().BeTrue();
        state.IsEnabled.Should().BeTrue();
    }

    [Test]
    public void GetState_HiddenForFolderClicks_EvenWithFlagEnabled()
    {
        // Folders cannot have sidecars; the option is file-only.
        SetFeatureFlag(enabled: true);
        var folder = Substitute.For<IFolderResource>();

        var state = CreateOption().GetState(ContextFor(folder));

        state.IsVisible.Should().BeFalse();
        state.IsEnabled.Should().BeFalse();
    }
}
