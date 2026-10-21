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

        // The temp folder is the project folder. The substitute resource file system resolves each key through the
        // project root's real handler, and reads the real disk.
        var rootHandlers = TestRootHandlers.Create(_tempFolder);

        var localFileSystem = TestFileSystem.CreateLocal();
        var resourceFileSystem = Substitute.For<IResourceFileSystem>();
        resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>()).Returns(callInfo =>
        {
            var resource = callInfo.Arg<ResourceKey>();
            var resolveResult = rootHandlers.ResolveResourcePath(resource);
            if (resolveResult.IsFailure)
            {
                return Task.FromResult(Result<StorageItemInfo>.Fail(resolveResult.FirstErrorMessage));
            }

            return localFileSystem.GetInfoAsync(resolveResult.Value);
        });

        var resourceOperations = Substitute.For<IResourceOperationService>();
        resourceOperations.CanCreateResource(Arg.Any<ResourceKey>(), Arg.Any<bool>()).Returns(Celbridge.Core.Result.Ok());

        var resourceService = Substitute.For<IResourceService>();
        resourceService.RootHandlers.Returns(rootHandlers);
        resourceService.FileSystem.Returns(resourceFileSystem);
        resourceService.Operations.Returns(resourceOperations);

        _packageService = Substitute.For<IPackageService>();
        _packageService.GetAllPackages().Returns([]);
        _packageService.GetPackageFolders().Returns([new ResourceKey("packages"), new ResourceKey("lib")]);

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

    [TestCase("elsewhere")]
    [TestCase("lib/nested")]
    [TestCase("temp:package-staging")]
    public async Task InstallPackage_DestinationThatIsNotAPackageFolder_IsRefusedBeforeReachingTheWorkshop(string destination)
    {
        var tools = new WorkshopTools(_services);
        var result = await tools.InstallPackage("acme-widget", destination: destination, confirmWithUser: false);

        result.IsError.Should().BeTrue();
        TextOf(result).Should().Contain("is not a package folder");
        TextOf(result).Should().Contain("'project:packages', 'project:lib'");
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
