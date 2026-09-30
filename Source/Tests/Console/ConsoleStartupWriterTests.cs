using Celbridge.Console.Helpers;
using Celbridge.Resources.Helpers;
using Celbridge.Tests.FileSystem;

namespace Celbridge.Tests.Console;

[TestFixture]
public class ConsoleStartupWriterTests
{
    private ILocalFileSystem _fileSystem = null!;
    private string _dataFolder = null!;

    [SetUp]
    public void Setup()
    {
        _fileSystem = TestFileSystem.CreateLocal();
        _dataFolder = Path.Combine(Path.GetTempPath(), "celbridge-startup-writer-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_dataFolder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dataFolder))
        {
            Directory.Delete(_dataFolder, recursive: true);
        }
    }

    [Test]
    public async Task WriteAsync_WritesTheModesFilesAndTheHistoryFolder()
    {
        var result = await ConsoleStartupWriter.WriteAsync(_fileSystem, _dataFolder, useShellProfile: false);

        result.IsSuccess.Should().BeTrue();
        var options = result.Value;
        options.ModeFolder.Should().Be(Path.Combine(_dataFolder, "console", "clean"));
        Directory.Exists(options.HistoryFolder).Should().BeTrue();
        File.Exists(Path.Combine(options.ModeFolder, "zsh", ".zshenv")).Should().BeTrue();
        File.Exists(Path.Combine(options.ModeFolder, "bash", "bashrc")).Should().BeTrue();
    }

    [Test]
    public async Task WriteAsync_RewritesAFileThatWasChanged()
    {
        var first = await ConsoleStartupWriter.WriteAsync(_fileSystem, _dataFolder, useShellProfile: true);
        var zshrcPath = Path.Combine(first.Value.ModeFolder, "zsh", ".zshrc");
        var generated = File.ReadAllText(zshrcPath);
        File.WriteAllText(zshrcPath, "# edited by hand\n");

        await ConsoleStartupWriter.WriteAsync(_fileSystem, _dataFolder, useShellProfile: true);

        File.ReadAllText(zshrcPath).Should().Be(generated);
    }

    [Test]
    public async Task DataFolderGitIgnore_IgnoresEverything_UnlessTheFolderHasOne()
    {
        var gitIgnorePath = Path.Combine(_dataFolder, ".gitignore");

        (await DataFolderGitIgnore.EnsureAsync(_fileSystem, _dataFolder)).IsSuccess.Should().BeTrue();
        File.ReadAllText(gitIgnorePath).Should().Be("*\n");

        File.WriteAllText(gitIgnorePath, "logs/\n");
        await DataFolderGitIgnore.EnsureAsync(_fileSystem, _dataFolder);
        File.ReadAllText(gitIgnorePath).Should().Be("logs/\n");
    }
}
