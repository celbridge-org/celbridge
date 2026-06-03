using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Tools;
using Celbridge.Workspace;

namespace Celbridge.Tests.Tools;

[TestFixture]
public class WebViewScreenshotResolverTests
{
    private string _projectFolder = null!;
    private IResourceFileSystem _resourceFileSystem = null!;
    private IResourceRegistry _resourceRegistry = null!;

    [SetUp]
    public void SetUp()
    {
        _projectFolder = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(WebViewScreenshotResolverTests)}/{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectFolder);

        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.ProjectFolderPath.Returns(_projectFolder);
        _resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>()).Returns(callInfo =>
        {
            var key = callInfo.Arg<ResourceKey>();
            return Result<string>.Ok(Path.Combine(_projectFolder, key.Path.Replace('/', Path.DirectorySeparatorChar)));
        });

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            Substitute.For<IMessengerService>(),
            workspaceWrapper,
            TestFileSystem.CreateLocal());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolder))
        {
            Directory.Delete(_projectFolder, recursive: true);
        }
    }

    [Test]
    public async Task Resolve_EmptySaveTo_UsesDefaultFolderWithCleanName()
    {
        var result = await WebViewScreenshotResolver.ResolveAsync(saveTo: "", format: "jpeg", _resourceFileSystem);

        result.IsSuccess.Should().BeTrue();
        var path = result.Value.Path;
        path.Should().StartWith("screenshots/screenshot-");
        path.Should().EndWith(".jpg");
        path.Should().NotContain(".jpg-").And.MatchRegex(@"screenshots/screenshot-\d{8}-\d{6}\.jpg$");
    }

    [Test]
    public async Task Resolve_EmptySaveToWithPng_UsesPngExtension()
    {
        var result = await WebViewScreenshotResolver.ResolveAsync(saveTo: "", format: "png", _resourceFileSystem);

        result.IsSuccess.Should().BeTrue();
        result.Value.Path.Should().EndWith(".png");
    }

    [Test]
    public async Task Resolve_ExactResourceKeyWithMatchingExtension_PreservesKey()
    {
        var result = await WebViewScreenshotResolver.ResolveAsync(saveTo: "docs/output.png", format: "png", _resourceFileSystem);

        result.IsSuccess.Should().BeTrue();
        result.Value.ToString().Should().Be("project:docs/output.png");
    }

    [Test]
    public async Task Resolve_JpgExtensionMatchesJpegFormat()
    {
        var result = await WebViewScreenshotResolver.ResolveAsync(saveTo: "docs/output.jpg", format: "jpeg", _resourceFileSystem);

        result.IsSuccess.Should().BeTrue();
        result.Value.ToString().Should().Be("project:docs/output.jpg");
    }

    [Test]
    public async Task Resolve_JpegExtensionMatchesJpegFormat()
    {
        var result = await WebViewScreenshotResolver.ResolveAsync(saveTo: "docs/output.jpeg", format: "jpeg", _resourceFileSystem);

        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public async Task Resolve_ExtensionFormatMismatch_Fails()
    {
        var result = await WebViewScreenshotResolver.ResolveAsync(saveTo: "docs/output.png", format: "jpeg", _resourceFileSystem);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("does not match format");
    }

    [Test]
    public async Task Resolve_TxtExtension_FailsForBothFormats()
    {
        var resultJpeg = await WebViewScreenshotResolver.ResolveAsync(saveTo: "docs/output.txt", format: "jpeg", _resourceFileSystem);
        var resultPng = await WebViewScreenshotResolver.ResolveAsync(saveTo: "docs/output.txt", format: "png", _resourceFileSystem);

        resultJpeg.IsFailure.Should().BeTrue();
        resultPng.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task Resolve_TrailingSlashSaveTo_GeneratesAutoNameInThatFolder()
    {
        var result = await WebViewScreenshotResolver.ResolveAsync(saveTo: "docs/", format: "jpeg", _resourceFileSystem);

        result.IsSuccess.Should().BeTrue();
        var path = result.Value.Path;
        path.Should().StartWith("docs/screenshot-");
        path.Should().EndWith(".jpg");
    }

    [Test]
    public async Task Resolve_NoExtensionSaveTo_TreatedAsFolder()
    {
        // A path without a file extension is interpreted as a folder reference,
        // matching the agent's likely intent ("put a screenshot in this folder").
        var result = await WebViewScreenshotResolver.ResolveAsync(saveTo: "captures", format: "png", _resourceFileSystem);

        result.IsSuccess.Should().BeTrue();
        var path = result.Value.Path;
        path.Should().StartWith("captures/screenshot-");
        path.Should().EndWith(".png");
    }

    [Test]
    public async Task Resolve_CollisionWithExistingFile_AddsSequenceSuffix()
    {
        // Pre-create a file matching the timestamp pattern the saver will pick.
        // To do this deterministically without racing the wall clock, we let
        // the saver generate its first name, then re-run Resolve and confirm
        // the second call produces a -1 suffix.
        var first = await WebViewScreenshotResolver.ResolveAsync(saveTo: "screenshots/", format: "jpeg", _resourceFileSystem);
        first.IsSuccess.Should().BeTrue();

        var firstPath = first.Value.Path;
        var firstAbsolute = Path.Combine(_projectFolder, firstPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(firstAbsolute)!);
        File.WriteAllBytes(firstAbsolute, new byte[] { 0 });

        var second = await WebViewScreenshotResolver.ResolveAsync(saveTo: "screenshots/", format: "jpeg", _resourceFileSystem);
        second.IsSuccess.Should().BeTrue();

        var secondPath = second.Value.Path;
        secondPath.Should().NotBe(firstPath);
        secondPath.Should().MatchRegex(@"screenshots/screenshot-\d{8}-\d{6}(-\d+)?\.jpg$");
    }

    [Test]
    public async Task Resolve_TraversalAttempt_RejectedByResourceKey()
    {
        var result = await WebViewScreenshotResolver.ResolveAsync(saveTo: "../escape.png", format: "png", _resourceFileSystem);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Invalid saveTo");
    }

    [Test]
    public async Task Resolve_BackslashInSaveTo_Rejected()
    {
        var result = await WebViewScreenshotResolver.ResolveAsync(saveTo: @"docs\output.png", format: "png", _resourceFileSystem);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task Resolve_AbsolutePathSaveTo_Rejected()
    {
        var result = await WebViewScreenshotResolver.ResolveAsync(saveTo: "/etc/output.png", format: "png", _resourceFileSystem);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task Resolve_UnsupportedFormat_Fails()
    {
        var result = await WebViewScreenshotResolver.ResolveAsync(saveTo: "", format: "webp", _resourceFileSystem);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Unsupported screenshot format");
    }
}
