using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Celbridge.Console.Helpers;
using Celbridge.Resources.Helpers;
using Celbridge.Tests.FileSystem;

namespace Celbridge.Tests.Console;

[TestFixture]
public class ConsoleStartupWriterTests
{
    private static readonly ConsoleShell Zsh = new("/bin/zsh", ConsoleShellFamily.Posix);

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

    [TestCase("/bin/zsh", ConsoleShellFamily.Posix, "zsh/.zshenv")]
    [TestCase("/bin/bash", ConsoleShellFamily.Posix, "bash/bashrc")]
    [TestCase("powershell.exe", ConsoleShellFamily.PowerShell, "powershell/startup.ps1")]
    public async Task WriteAsync_WritesOnlyTheShellsFilesAndTheHistoryFolder(
        string executable,
        ConsoleShellFamily family,
        string expectedFile)
    {
        var shell = new ConsoleShell(executable, family);

        var result = await ConsoleStartupWriter.WriteAsync(_fileSystem, _dataFolder, shell);

        result.IsSuccess.Should().BeTrue();
        var options = result.Value;
        options.StartupFolder.Should().Be(Path.Combine(_dataFolder, "console"));
        options.HistoryFolder.Should().Be(Path.Combine(_dataFolder, "console", "history"));
        File.Exists(Path.Combine(options.StartupFolder, expectedFile)).Should().BeTrue();

        // Both modes share the shell's files, so there is no folder for either mode.
        var folders = Directory.GetDirectories(options.StartupFolder).Select(Path.GetFileName);
        folders.Should().BeEquivalentTo("history", Path.GetDirectoryName(expectedFile));
    }

    [Test]
    public async Task WriteAsync_RewritesAFileThatWasChanged()
    {
        var first = await ConsoleStartupWriter.WriteAsync(_fileSystem, _dataFolder, Zsh);
        var zshrcPath = Path.Combine(first.Value.StartupFolder, "zsh", ".zshrc");
        var generated = File.ReadAllText(zshrcPath);
        File.WriteAllText(zshrcPath, "# edited by hand\n");

        await ConsoleStartupWriter.WriteAsync(_fileSystem, _dataFolder, Zsh);

        File.ReadAllText(zshrcPath).Should().Be(generated);
    }

    [Test]
    public async Task WriteAsync_IntoANewFolder_ThrowsNothingForTheMissingFiles()
    {
        var thrown = new ConcurrentQueue<Exception>();
        void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (args.Exception.Message.Contains(_dataFolder, StringComparison.Ordinal))
            {
                thrown.Enqueue(args.Exception);
            }
        }

        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
        try
        {
            var result = await ConsoleStartupWriter.WriteAsync(_fileSystem, _dataFolder, Zsh);
            result.IsSuccess.Should().BeTrue();
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;
        }

        thrown.Should().BeEmpty("a file not written yet is probed rather than read");
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
