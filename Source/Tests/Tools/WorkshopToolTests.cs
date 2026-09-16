using Celbridge.Packages;
using Celbridge.Resources;
using Celbridge.Server;
using Celbridge.Settings;
using Celbridge.Tests.FileSystem;
using Celbridge.Tools;
using Celbridge.Workshop;
using Celbridge.Workspace;
using ModelContextProtocol.Protocol;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for the WorkshopTools MCP tool methods.
/// </summary>
[TestFixture]
public class WorkshopToolTests
{
    private string _tempFolder = null!;
    private IApplicationServiceProvider _services = null!;
    private IPackageService _packageService = null!;

    [SetUp]
    public void SetUp()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(WorkshopToolTests));
        Directory.CreateDirectory(_tempFolder);

        // Resource keys map onto the temp folder, and the resource file system reads the real disk through the
        // same mapping.
        var resourceRegistry = Substitute.For<IResourceRegistry>();
        resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>(), Arg.Any<bool>()).Returns(callInfo =>
        {
            var resource = callInfo.Arg<ResourceKey>();
            return Result<string>.Ok(ResolvePath(resource));
        });
        resourceRegistry.GetResourceKey(Arg.Any<string>()).Returns(callInfo =>
        {
            var path = callInfo.Arg<string>();
            var relativePath = Path.GetRelativePath(_tempFolder, path).Replace(Path.DirectorySeparatorChar, '/');
            return Result<ResourceKey>.Ok(new ResourceKey(relativePath));
        });

        var localFileSystem = TestFileSystem.CreateLocal();
        var resourceFileSystem = Substitute.For<IResourceFileSystem>();
        resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>()).Returns(callInfo =>
        {
            var resource = callInfo.Arg<ResourceKey>();
            return localFileSystem.GetInfoAsync(ResolvePath(resource));
        });

        var resourceOperations = Substitute.For<IResourceOperationService>();
        resourceOperations.CanCreateResource(Arg.Any<ResourceKey>(), Arg.Any<bool>()).Returns(Celbridge.Core.Result.Ok());

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(resourceRegistry);
        resourceService.FileSystem.Returns(resourceFileSystem);
        resourceService.Operations.Returns(resourceOperations);

        _packageService = Substitute.For<IPackageService>();
        _packageService.GetAllPackages().Returns([]);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        workspaceService.PackageService.Returns(_packageService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var settingsService = Substitute.For<ISettingsService>();
        settingsService.Get(SettingCatalog.Workshop.Author).Returns("Acme");

        _services = Substitute.For<IApplicationServiceProvider>();
        _services.GetRequiredService<IWorkspaceWrapper>().Returns(workspaceWrapper);
        _services.GetRequiredService<ISettingsService>().Returns(settingsService);
        _services.GetRequiredService<ILocalFileSystem>().Returns(localFileSystem);
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
    public async Task PublishPackage_MalformedPackageVersion_IsRefusedBeforeReachingTheWorkshop(string versionLine, string expectedError)
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

        var tools = new WorkshopTools(_services);
        var result = await tools.PublishPackage("packages/acme-widget", confirmWithUser: false);

        result.IsError.Should().BeTrue();
        TextOf(result).Should().Be(expectedError);
        _services.DidNotReceive().GetRequiredService<IPackageApiClient>();
    }

    [Test]
    public async Task InstallPackage_SameNamePackageLoadedAtAnotherPath_IsRefusedBeforeReachingTheWorkshop()
    {
        var loadedFolder = WriteProjectPackage("acme-widget");
        _packageService.GetAllPackages().Returns([CreateProjectPackage("acme-widget", loadedFolder)]);

        var tools = new WorkshopTools(_services);
        var result = await tools.InstallPackage("acme-widget", destination: "lib", confirmWithUser: false);

        result.IsError.Should().BeTrue();
        TextOf(result).Should().Contain("already installed in the project at 'project:packages/acme-widget'");
        _services.DidNotReceive().GetRequiredService<IPackageApiClient>();
    }

    [Test]
    public async Task InstallPackage_SameNamePackageWhoseManifestWasDeleted_IsNotRefused()
    {
        // The registry still lists the package as the project loaded, but its manifest has gone, so the copy
        // cannot fault the next load.
        var loadedFolder = WriteProjectPackage("acme-widget");
        _packageService.GetAllPackages().Returns([CreateProjectPackage("acme-widget", loadedFolder)]);
        File.Delete(Path.Combine(loadedFolder, "package.toml"));

        var packageApiClient = Substitute.For<IPackageApiClient>();
        packageApiClient.GetPackageAsync("acme-widget")
            .Returns(Result<RemotePackageDetails>.Fail("Package 'acme-widget' was not found on the workshop."));
        _services.GetRequiredService<IPackageApiClient>().Returns(packageApiClient);

        var tools = new WorkshopTools(_services);
        var result = await tools.InstallPackage("acme-widget", destination: "lib", confirmWithUser: false);

        // The install passed the duplicate check and stopped at the workshop lookup instead.
        TextOf(result).Should().Be("Package 'acme-widget' was not found on the workshop.");
    }

    private string ResolvePath(ResourceKey resource)
    {
        return Path.Combine(_tempFolder, resource.Path.Replace('/', Path.DirectorySeparatorChar));
    }

    // Writes a manifest for a package in packages/ under the temp folder and returns the package folder.
    private string WriteProjectPackage(string packageName)
    {
        var packageFolder = Path.Combine(_tempFolder, "packages", packageName);
        Directory.CreateDirectory(packageFolder);
        File.WriteAllText(Path.Combine(packageFolder, "package.toml"), $"[package]\nname = \"{packageName}\"\n");
        return packageFolder;
    }

    private static Package CreateProjectPackage(string packageName, string packageFolder)
    {
        return new Package
        {
            Info = new PackageInfo
            {
                Name = packageName,
                Origin = PackageOrigin.Project,
                PackageFolder = packageFolder
            }
        };
    }

    private static string TextOf(CallToolResult result)
    {
        return result.Content.OfType<TextContentBlock>().Single().Text;
    }
}
