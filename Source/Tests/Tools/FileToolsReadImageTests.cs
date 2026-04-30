using System.Text.Json;
using Celbridge.Resources;
using Celbridge.Server;
using Celbridge.Tools;
using Celbridge.Workspace;
using ModelContextProtocol.Protocol;

namespace Celbridge.Tests.Tools;

[TestFixture]
public class FileToolsReadImageTests
{
    // Minimal JPEG (SOI + JFIF header + EOI) for the happy-path read.
    private static readonly byte[] MinimalJpegBytes =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00,
        0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        0xFF, 0xD9
    ];

    private IApplicationServiceProvider _services = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private string _tempFolder = null!;

    [SetUp]
    public void SetUp()
    {
        _services = Substitute.For<IApplicationServiceProvider>();

        _tempFolder = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(FileToolsReadImageTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);

        _resourceRegistry = Substitute.For<IResourceRegistry>();

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _services.GetRequiredService<IWorkspaceWrapper>().Returns(workspaceWrapper);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, recursive: true);
        }
    }

    [Test]
    public async Task ReadImage_MissingFile_Fails()
    {
        var resourceKey = ResourceKey.Create("captures/missing.png");
        var resourcePath = Path.Combine(_tempFolder, "captures", "missing.png");
        StubResolve(resourceKey, resourcePath);

        var tools = new FileTools(_services);
        var result = await tools.ReadImage(resourceKey.ToString());

        result.IsError.Should().BeTrue();
        GetText(result).Should().Contain("File not found");
    }

    [Test]
    public async Task ReadImage_UnsupportedExtension_Fails()
    {
        var resourceKey = ResourceKey.Create("docs/notes.txt");
        var resourcePath = Path.Combine(_tempFolder, "notes.txt");
        File.WriteAllText(resourcePath, "not an image");
        StubResolve(resourceKey, resourcePath);

        var tools = new FileTools(_services);
        var result = await tools.ReadImage(resourceKey.ToString());

        result.IsError.Should().BeTrue();
        var text = GetText(result);
        text.Should().Contain("does not support extension");
        text.Should().Contain(".txt");
        text.Should().Contain("file_read_binary");
    }

    [Test]
    public async Task ReadImage_OversizeFile_Fails()
    {
        var resourceKey = ResourceKey.Create("captures/big.jpg");
        var resourcePath = Path.Combine(_tempFolder, "big.jpg");
        // 6 MB exceeds the 5 MB inline cap.
        const int sixMegabytes = 6 * 1024 * 1024;
        File.WriteAllBytes(resourcePath, new byte[sixMegabytes]);
        StubResolve(resourceKey, resourcePath);

        var tools = new FileTools(_services);
        var result = await tools.ReadImage(resourceKey.ToString());

        result.IsError.Should().BeTrue();
        var text = GetText(result);
        text.Should().Contain("exceeds");
        text.Should().Contain("inline cap");
    }

    [Test]
    public async Task ReadImage_InvalidResourceKey_Fails()
    {
        var tools = new FileTools(_services);
        var result = await tools.ReadImage("../escape.png");

        result.IsError.Should().BeTrue();
        GetText(result).Should().Contain("Invalid resource key");
    }

    [Test]
    public async Task ReadImage_HappyPath_ReturnsImageAndMetadata()
    {
        var resourceKey = ResourceKey.Create("captures/sample.jpg");
        var resourcePath = Path.Combine(_tempFolder, "sample.jpg");
        File.WriteAllBytes(resourcePath, MinimalJpegBytes);
        StubResolve(resourceKey, resourcePath);

        var tools = new FileTools(_services);
        var result = await tools.ReadImage(resourceKey.ToString());

        // IsError is null on success; only set to true on failure.
        result.IsError.Should().NotBe(true);
        var imageBlock = result.Content.OfType<ImageContentBlock>().Single();
        imageBlock.MimeType.Should().Be("image/jpeg");

        var metadataJson = result.Content.OfType<TextContentBlock>().Single().Text;
        var metadata = JsonDocument.Parse(metadataJson).RootElement;
        metadata.GetProperty("resource").GetString().Should().Be("captures/sample.jpg");
        metadata.GetProperty("mimeType").GetString().Should().Be("image/jpeg");
        metadata.GetProperty("sizeBytes").GetInt32().Should().Be(MinimalJpegBytes.Length);
    }

    private void StubResolve(ResourceKey resourceKey, string resourcePath)
    {
        _resourceRegistry
            .ResolveResourcePath(resourceKey)
            .Returns(Result<string>.Ok(resourcePath));
    }

    private static string GetText(CallToolResult result)
    {
        return result.Content.OfType<TextContentBlock>().Single().Text;
    }
}
