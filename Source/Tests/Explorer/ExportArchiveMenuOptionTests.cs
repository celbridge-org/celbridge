using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Dialog;
using Celbridge.Explorer.Menu;
using Celbridge.Explorer.Menu.Options;
using Celbridge.Projects;
using Celbridge.Resources;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Explorer;

/// <summary>
/// Unit tests for ExportArchiveMenuOption, which offers the project folder for export and passes the path
/// chosen in the save dialog to the export command.
/// </summary>
[TestFixture]
public class ExportArchiveMenuOptionTests
{
    private static readonly ResourceItem ProjectFolder = new(ResourceKey.Empty, IsFolder: true);

    private IStringLocalizer _stringLocalizer = null!;
    private ICommandService _commandService = null!;
    private IProjectService _projectService = null!;
    private IFilePickerService _filePickerService = null!;
    private IExportArchiveCommand _exportCommand = null!;

    [SetUp]
    public void Setup()
    {
        var project = Substitute.For<IProject>();
        project.ProjectFolderPath.Returns(Path.Combine(Path.GetTempPath(), "Acme"));

        _projectService = Substitute.For<IProjectService>();
        _projectService.CurrentProject.Returns(project);

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
        var folder = new ResourceItem(new ResourceKey("Docs"), IsFolder: true);

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
            _projectService,
            _filePickerService,
            Substitute.For<ILogger<ExportArchiveMenuOption>>());
    }

    private ExplorerMenuContext ProjectFolderContext()
    {
        return new ExplorerMenuContext(
            ClickedResource: ProjectFolder,
            SelectedResources: Array.Empty<ResourceItem>(),
            IsProjectFolderTargeted: true,
            HasClipboardData: false,
            ClipboardContentType: ClipboardContentType.None,
            ClipboardOperation: ClipboardContentOperation.None);
    }

    private static ExplorerMenuContext SelectedResourceContext(ResourceItem selectedResource)
    {
        var selectedResources = new List<ResourceItem>
        {
            selectedResource
        };

        return new ExplorerMenuContext(
            ClickedResource: selectedResource,
            SelectedResources: selectedResources,
            IsProjectFolderTargeted: false,
            HasClipboardData: false,
            ClipboardContentType: ClipboardContentType.None,
            ClipboardOperation: ClipboardContentOperation.None);
    }
}
