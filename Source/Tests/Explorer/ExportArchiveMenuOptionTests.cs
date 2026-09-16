using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Dialog;
using Celbridge.Explorer.Menu;
using Celbridge.Explorer.Menu.Options;
using Celbridge.Resources;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Explorer;

/// <summary>
/// Unit tests for ExportArchiveMenuOption, which offers the project folder for export and passes the path
/// chosen in the save dialog to the export command.
/// </summary>
[TestFixture]
public class ExportArchiveMenuOptionTests
{
    private IFolderResource _projectFolder = null!;
    private IStringLocalizer _stringLocalizer = null!;
    private ICommandService _commandService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IFilePickerService _filePickerService = null!;
    private IExportArchiveCommand _exportCommand = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolder = Substitute.For<IFolderResource>();

        var resourceRegistry = Substitute.For<IResourceRegistry>();
        resourceRegistry.ProjectFolderPath.Returns(Path.Combine(Path.GetTempPath(), "Acme"));
        resourceRegistry.GetResourceKey(_projectFolder).Returns(ResourceKey.Empty);

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _stringLocalizer = Substitute.For<IStringLocalizer>();
        _stringLocalizer[Arg.Any<string>()].Returns(callInfo =>
        {
            var name = (string)callInfo[0];
            return new LocalizedString(name, name);
        });

        _filePickerService = Substitute.For<IFilePickerService>();

        _exportCommand = Substitute.For<IExportArchiveCommand>();
        _commandService = Substitute.For<ICommandService>();
        _commandService
            .When(service => service.Execute(Arg.Any<Action<IExportArchiveCommand>?>(), Arg.Any<string>(), Arg.Any<int>()))
            .Do(callInfo =>
            {
                var configure = callInfo.Arg<Action<IExportArchiveCommand>?>();
                configure?.Invoke(_exportCommand);
            });
    }

    [Test]
    public void GetState_VisibleWhenProjectFolderTargeted()
    {
        var state = CreateOption().GetState(ProjectFolderContext());

        state.IsVisible.Should().BeTrue();
        state.IsEnabled.Should().BeTrue();
    }

    [Test]
    public void GetState_HiddenForSelectedFolder()
    {
        var folder = Substitute.For<IFolderResource>();

        var state = CreateOption().GetState(SelectedResourceContext(folder));

        state.IsVisible.Should().BeFalse();
        state.IsEnabled.Should().BeFalse();
    }

    [Test]
    public void Execute_ExportsProjectFolderToChosenFile()
    {
        string? suggestedFileName = null;
        var archiveFilePath = Path.Combine(Path.GetTempPath(), "Exports", "Acme.zip");
        _filePickerService
            .PickSaveFileAsync(Arg.Do<string>(fileName => suggestedFileName = fileName), Arg.Any<string>(), Arg.Any<IEnumerable<string>>())
            .Returns(Task.FromResult(Result<string>.Ok(archiveFilePath)));

        CreateOption().Execute(ProjectFolderContext());

        suggestedFileName.Should().Be("Acme.zip");
        _exportCommand.SourceResource.Should().Be(ResourceKey.Empty);
        _exportCommand.ArchiveFilePath.Should().Be(archiveFilePath);
    }

    [Test]
    public void Execute_WhenSaveDialogIsCancelled_ExportsNothing()
    {
        _filePickerService
            .PickSaveFileAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>>())
            .Returns(Task.FromResult(Result<string>.Fail("No file selected to save")));

        CreateOption().Execute(ProjectFolderContext());

        _commandService.DidNotReceiveWithAnyArgs().Execute<IExportArchiveCommand>();
    }

    private ExportArchiveMenuOption CreateOption()
    {
        return new ExportArchiveMenuOption(
            _stringLocalizer,
            _commandService,
            _workspaceWrapper,
            _filePickerService,
            Substitute.For<ILogger<ExportArchiveMenuOption>>());
    }

    private ExplorerMenuContext ProjectFolderContext()
    {
        return new ExplorerMenuContext(
            ClickedResource: _projectFolder,
            SelectedResources: Array.Empty<IResource>(),
            ProjectFolder: _projectFolder,
            IsProjectFolderTargeted: true,
            HasClipboardData: false,
            ClipboardContentType: ClipboardContentType.None,
            ClipboardOperation: ClipboardContentOperation.None);
    }

    private ExplorerMenuContext SelectedResourceContext(IResource selectedResource)
    {
        var selectedResources = new List<IResource>
        {
            selectedResource
        };

        return new ExplorerMenuContext(
            ClickedResource: selectedResource,
            SelectedResources: selectedResources,
            ProjectFolder: _projectFolder,
            IsProjectFolderTargeted: false,
            HasClipboardData: false,
            ClipboardContentType: ClipboardContentType.None,
            ClipboardOperation: ClipboardContentOperation.None);
    }
}
