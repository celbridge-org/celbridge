using System.IO.Compression;
using Celbridge.Localization;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Helpers;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for ExtractArchiveCommand over a real resource file system and the default resource policy. The
/// operation service is a substitute, so each test reads the extracted files from the substitute's received calls.
/// </summary>
[TestFixture]
public class ExtractArchiveCommandTests
{
    private string _projectFolderPath = null!;
    private IResourceOperationService _operationService = null!;
    private IMessengerService _messengerService = null!;
    private ILogger<ExtractArchiveCommand> _logger = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ExtractArchiveCommandTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        _operationService = Substitute.For<IResourceOperationService>();
        _operationService.CreateFolderAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result.Ok()));
        _operationService.CreateFileAsync(Arg.Any<ResourceKey>(), Arg.Any<byte[]>())
            .Returns(Task.FromResult(Result.Ok()));

        _messengerService = Substitute.For<IMessengerService>();
        _logger = Substitute.For<ILogger<ExtractArchiveCommand>>();

        var resourceService = Substitute.For<IResourceService>();
        resourceService.RootHandlers.Returns(TestRootHandlers.Create(_projectFolderPath));
        resourceService.Operations.Returns(_operationService);
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            Substitute.For<IMessengerService>(),
            _workspaceWrapper,
            TestFileSystem.CreateLocal());
        resourceService.FileSystem.Returns(resourceFileSystem);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolderPath))
        {
            Directory.Delete(_projectFolderPath, recursive: true);
        }
    }

    [Test]
    public async Task Extract_EntryInsideGitFolder_IsSkippedAndTheRestExtracted()
    {
        WriteArchive("Acme.zip", "readme.md", ".git/config");

        var command = CreateCommand();
        command.ArchiveResource = new ResourceKey("Acme.zip");
        command.DestinationResource = new ResourceKey("Extracted");

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.Entries.Should().Be(1);
        command.ResultValue.SkippedEntries.Should().Equal(".git/config");

        var createdFiles = _operationService.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IResourceOperationService.CreateFileAsync))
            .Select(call => call.GetArguments()[0])
            .ToList();
        createdFiles.Should().Equal(new ResourceKey("Extracted/readme.md"));

        _messengerService.Received(1).Send(Arg.Is<ArchiveEntriesSkippedMessage>(message =>
            message.ArchiveResource == new ResourceKey("Acme.zip")
            && message.SkippedEntries.SequenceEqual(new[] { ".git/config" })));

        _logger.Received(1).LogWarning(
            Arg.Is<string?>(message => message != null
                && message.StartsWith("Skipped 1 entry of 'project:Acme.zip'")),
            Arg.Any<object?[]>());
    }

    [Test]
    public async Task Extract_ManySkippedEntries_AreLoggedOnOneCappedLine()
    {
        var gitEntries = Enumerable.Range(1, 12).Select(index => $".git/objects/{index:D2}").ToArray();
        WriteArchive("Acme.zip", ["readme.md", .. gitEntries]);

        var command = CreateCommand();
        command.ArchiveResource = new ResourceKey("Acme.zip");
        command.DestinationResource = new ResourceKey("Extracted");

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        _logger.Received(1).LogWarning(
            Arg.Is<string?>(message => message != null
                && message.StartsWith("Skipped 12 entries of 'project:Acme.zip'")
                && message.Contains("('.git')")
                && message.Contains(".git/objects/10")
                && !message.Contains(".git/objects/11")
                && message.EndsWith(" and 2 more")),
            Arg.Any<object?[]>());
    }

    [Test]
    public async Task Extract_WithNothingSkipped_SendsNoSkippedMessage()
    {
        WriteArchive("Acme.zip", "readme.md");

        var command = CreateCommand();
        command.ArchiveResource = new ResourceKey("Acme.zip");
        command.DestinationResource = new ResourceKey("Extracted");

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.SkippedEntries.Should().BeEmpty();
        _messengerService.DidNotReceive().Send(Arg.Any<ArchiveEntriesSkippedMessage>());
    }

    private ExtractArchiveCommand CreateCommand()
    {
        var operationNotifier = new ResourceOperationNotifier(
            Substitute.For<ILogger<ResourceOperationNotifier>>(),
            _messengerService,
            Substitute.For<IProjectService>(),
            Substitute.For<IReportWriter>(),
            Substitute.For<ILocalizerService>());

        return new ExtractArchiveCommand(
            _logger,
            _workspaceWrapper,
            operationNotifier);
    }

    private void WriteArchive(string archiveName, params string[] entryNames)
    {
        var archivePath = Path.Combine(_projectFolderPath, archiveName);

        using var zipArchive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var entryName in entryNames)
        {
            var entry = zipArchive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(entryName);
        }
    }
}
