using Celbridge.Automation;
using Celbridge.FileSystem.Services;
using Celbridge.Platform;
using Celbridge.Python.Services;
using Celbridge.Tests.Helpers;

namespace Celbridge.Tests.Console;

/// <summary>
/// Covers removing a REPL's temporary environment in a temporary data folder. Only a folder directly inside uv's
/// temporary environments folder is removed.
/// </summary>
[TestFixture]
public class TemporaryEnvironmentsTests
{
    private string _rootFolderPath = null!;
    private CelbridgeWheelFolders _folders = null!;
    private TemporaryEnvironments _temporaryEnvironments = null!;

    [SetUp]
    public void SetUp()
    {
        _rootFolderPath = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(TemporaryEnvironmentsTests), Guid.NewGuid().ToString("N"));

        var appEnvironment = Substitute.For<IAppEnvironment>();
        appEnvironment.LocalApplicationDataFolderPath.Returns(_rootFolderPath);
        _folders = new CelbridgeWheelFolders(appEnvironment);

        _temporaryEnvironments = new TemporaryEnvironments(
            new LocalFileSystem(new NullLogger<LocalFileSystem>()),
            new NullLogger<TemporaryEnvironments>(),
            Substitute.For<IAutomationService>(),
            _folders);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_rootFolderPath))
        {
            Directory.Delete(_rootFolderPath, recursive: true);
        }
    }

    [Test]
    public async Task Remove_ATemporaryEnvironment_RemovesIt()
    {
        var environmentPath = CreateFolderWithAFile(Path.Combine(_folders.TemporaryEnvironmentsFolderPath, ".tmp1234"));

        await _temporaryEnvironments.RemoveAsync(environmentPath);

        Directory.Exists(environmentPath).Should().BeFalse();
    }

    [Test]
    public async Task Remove_AFolderOutsideTheTemporaryEnvironments_LeavesItInPlace()
    {
        var folderPath = CreateFolderWithAFile(Path.Combine(_rootFolderPath, "Projects", ".tmp1234"));

        await _temporaryEnvironments.RemoveAsync(folderPath);

        Directory.Exists(folderPath).Should().BeTrue();
    }

    [Test]
    public async Task Remove_TheFolderThatHoldsTheTemporaryEnvironments_LeavesItInPlace()
    {
        CreateFolderWithAFile(Path.Combine(_folders.TemporaryEnvironmentsFolderPath, ".tmp1234"));

        await _temporaryEnvironments.RemoveAsync(_folders.TemporaryEnvironmentsFolderPath);

        Directory.Exists(_folders.TemporaryEnvironmentsFolderPath).Should().BeTrue();
    }

    private static string CreateFolderWithAFile(string folderPath)
    {
        Directory.CreateDirectory(folderPath);
        File.WriteAllText(Path.Combine(folderPath, "pyvenv.cfg"), "home = /store/cpython-3.13");

        return folderPath;
    }
}
