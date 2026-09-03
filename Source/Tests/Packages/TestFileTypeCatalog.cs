using Celbridge.FileSystem;
using Celbridge.Packages;
using Celbridge.Platform;
using Celbridge.Tests.FileSystem;

namespace Celbridge.Tests.Packages;

/// <summary>
/// Test-side factory for a file type catalog loaded from the bundled file-types.json, so tests see the
/// same data the application does.
/// </summary>
internal static class TestFileTypeCatalog
{
    public static FileTypeCatalog CreateLoaded()
    {
        var appEnvironment = Substitute.For<IAppEnvironment>();
        appEnvironment.SharedWebAssetsFolderPath.Returns(
            Path.Combine(AppContext.BaseDirectory, "Celbridge.WebHost", "Web"));

        var catalog = new FileTypeCatalog(
            Substitute.For<ILogger<FileTypeCatalog>>(),
            TestFileSystem.CreateLocal(),
            appEnvironment);

        catalog.LoadAsync().GetAwaiter().GetResult();

        return catalog;
    }

    public static ITextBinarySniffer CreateSniffer(ILocalFileSystem fileSystem)
    {
        return new TextBinarySniffer(fileSystem, CreateLoaded());
    }
}
