using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Verifies WriteBinaryFileCommand decodes base64 content and writes the bytes
/// directly to disk.
/// </summary>
[TestFixture]
public class WriteBinaryFileCommandTests
{
    private string _tempFolder = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(WriteBinaryFileCommandTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);

        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.ProjectFolderPath.Returns(_tempFolder);

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var fileWriter = new ResourceFileWriter(Substitute.For<ILogger<ResourceFileWriter>>(), _workspaceWrapper);
        resourceService.FileWriter.Returns(fileWriter);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, true);
        }
    }

    private WriteBinaryFileCommand CreateCommand()
    {
        return new WriteBinaryFileCommand(Substitute.For<ILogger<WriteBinaryFileCommand>>(), _workspaceWrapper);
    }

    [Test]
    public async Task ExecuteAsync_WritesDecodedBytesToDisk()
    {
        var resource = new ResourceKey("blobs/data.bin");
        var path = Path.Combine(_tempFolder, "data.bin");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var bytes = new byte[] { 0x01, 0x02, 0x03, 0xFF };
        var command = CreateCommand();
        command.FileResource = resource;
        command.Base64Content = Convert.ToBase64String(bytes);

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        File.Exists(path).Should().BeTrue();
        (await File.ReadAllBytesAsync(path)).Should().Equal(bytes);
        _resourceRegistry.Received(1).UpdateResourceRegistry();
    }

    [Test]
    public async Task ExecuteAsync_FailsOnInvalidBase64()
    {
        var resource = new ResourceKey("blobs/bad.bin");
        var command = CreateCommand();
        command.FileResource = resource;
        command.Base64Content = "not-base64!!!";

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
    }
}
