using Celbridge.Packages;
using Celbridge.Resources;
using Celbridge.Server;
using Celbridge.Settings;
using Celbridge.Tests.FileSystem;
using Celbridge.Tools;
using Celbridge.Workspace;
using ModelContextProtocol.Protocol;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for the PackageTools MCP tool methods.
/// </summary>
[TestFixture]
public class PackageToolTests
{
    private string _tempFolder = null!;
    private IApplicationServiceProvider _services = null!;

    [SetUp]
    public void SetUp()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(PackageToolTests));
        Directory.CreateDirectory(_tempFolder);

        var resourceRegistry = Substitute.For<IResourceRegistry>();
        resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>(), Arg.Any<bool>()).Returns(callInfo =>
        {
            var resource = callInfo.Arg<ResourceKey>();
            return Result<string>.Ok(Path.Combine(_tempFolder, resource.Path.Replace('/', Path.DirectorySeparatorChar)));
        });

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var settingsService = Substitute.For<ISettingsService>();
        settingsService.Get(SettingCatalog.Workshop.Author).Returns("Acme");

        _services = Substitute.For<IApplicationServiceProvider>();
        _services.GetRequiredService<IWorkspaceWrapper>().Returns(workspaceWrapper);
        _services.GetRequiredService<ISettingsService>().Returns(settingsService);
        _services.GetRequiredService<ILocalFileSystem>().Returns(TestFileSystem.CreateLocal());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, true);
        }
    }

    [TestCase("package-version = \"2.1\"", "'package-version': '2.1' is not a three-part version such as 1.0.0.")]
    [TestCase("package-version = 2", "'package-version' must be a string such as \"1.0.0\".")]
    public async Task Publish_MalformedPackageVersion_IsRefusedBeforeReachingTheWorkshop(string versionLine, string expectedError)
    {
        // The loader would fail the package once installed, so the manifest is refused before the
        // workshop client is acquired.
        var packageFolder = Path.Combine(_tempFolder, "packages", "acme-widget");
        Directory.CreateDirectory(packageFolder);
        File.WriteAllText(Path.Combine(packageFolder, "package.toml"), $"""
            [package]
            name = "acme-widget"
            {versionLine}
            """);

        var tools = new PackageTools(_services);
        var result = await tools.Publish("packages/acme-widget", confirmWithUser: false);

        result.IsError.Should().BeTrue();
        result.Content.OfType<TextContentBlock>().Single().Text.Should().Be(expectedError);
        _services.DidNotReceive().GetRequiredService<IPackageApiClient>();
    }
}
