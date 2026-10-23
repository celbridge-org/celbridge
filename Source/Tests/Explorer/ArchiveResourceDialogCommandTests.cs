using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Explorer.Commands;
using Celbridge.Resources;
using Celbridge.Validators;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Explorer;

/// <summary>
/// Unit tests for where the Create Archive dialog puts an archive: beside the folder, in its parent folder.
/// </summary>
[TestFixture]
public class ArchiveResourceDialogCommandTests
{
    private IResourceFileSystem _resourceFileSystem = null!;
    private IResourceNameValidator _validator = null!;
    private IServiceProvider _serviceProvider = null!;
    private IStringLocalizer _stringLocalizer = null!;
    private ICommandService _commandService = null!;
    private IDialogService _dialogService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IArchiveResourceCommand _archiveCommand = null!;

    private string? _proposedArchiveName;

    [SetUp]
    public void Setup()
    {
        _resourceFileSystem = Substitute.For<IResourceFileSystem>();

        var resourceService = Substitute.For<IResourceService>();
        resourceService.FileSystem.Returns(_resourceFileSystem);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _validator = Substitute.For<IResourceNameValidator>();
        _validator.SetParentFolderAsync(Arg.Any<ResourceKey>()).Returns(Result.Ok());
        _serviceProvider = Substitute.For<IServiceProvider>();
        _serviceProvider.GetService(typeof(IResourceNameValidator)).Returns(_validator);

        _stringLocalizer = Substitute.For<IStringLocalizer>();
        _stringLocalizer[Arg.Any<string>()].Returns(callInfo =>
        {
            var name = (string)callInfo[0];
            return new LocalizedString(name, name);
        });
        _stringLocalizer[Arg.Any<string>(), Arg.Any<object[]>()].Returns(callInfo =>
        {
            var name = (string)callInfo[0];
            return new LocalizedString(name, name);
        });

        // The user accepts the proposed name unchanged.
        _dialogService = Substitute.For<IDialogService>();
        _dialogService.ShowInputTextDialogAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Do<string>(defaultText => _proposedArchiveName = defaultText),
                Arg.Any<Range>(),
                Arg.Any<IValidator>(),
                Arg.Any<string?>())
            .Returns(callInfo => Task.FromResult(Result<string>.Ok(callInfo.ArgAt<string>(2))));

        _archiveCommand = Substitute.For<IArchiveResourceCommand>();
        _commandService = Substitute.For<ICommandService>();
        _commandService
            .When(service => service.Execute(Arg.Any<Action<IArchiveResourceCommand>?>(), Arg.Any<string>(), Arg.Any<int>()))
            .Do(callInfo =>
            {
                var configure = callInfo.Arg<Action<IArchiveResourceCommand>?>();
                configure?.Invoke(_archiveCommand);
            });
    }

    [Test]
    public async Task ExecuteAsync_ForSubfolder_ProposesArchiveBesideFolder()
    {
        var folderKey = new ResourceKey("Docs/Data");
        var resourceItem = new ResourceItem(folderKey, IsFolder: true);
        _resourceFileSystem.GetResourceItemAsync(folderKey).Returns(Result<ResourceItem>.Ok(resourceItem));

        var command = CreateCommand();
        command.FolderResource = folderKey;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        _proposedArchiveName.Should().Be("Data.zip");
        await _validator.Received(1).SetParentFolderAsync(new ResourceKey("Docs"));
        _archiveCommand.SourceResource.Should().Be(folderKey);
        _archiveCommand.ArchiveResource.Should().Be(new ResourceKey("Docs/Data.zip"));
    }

    private ArchiveResourceDialogCommand CreateCommand()
    {
        return new ArchiveResourceDialogCommand(
            Substitute.For<ILogger<ArchiveResourceDialogCommand>>(),
            _serviceProvider,
            _stringLocalizer,
            _commandService,
            _dialogService,
            _workspaceWrapper);
    }
}
