namespace Celbridge.Tests.FileSystem;

/// <summary>
/// Tests for moving a folder to another volume. A second volume is not guaranteed where the tests run,
/// so the tests call the copy that stands in for the rename directly.
/// </summary>
[TestFixture]
public class CrossVolumeFolderMoveTests
{
    private const int NotSameDeviceHResult = unchecked((int)0x80070011);
    private const int CrossDeviceLinkErrno = 18;
    private const int SharingViolationHResult = unchecked((int)0x80070020);

    private string _root = string.Empty;
    private ILogger _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"cel-move-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _logger = Substitute.For<ILogger>();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void IsCrossVolumeFailure_RecognizesTheRootCheck()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Only Windows paths have more than one root.");
        }

        // The exception .NET throws when the two roots differ, before it touches the disk.
        var exception = new IOException("Source and destination path must have identical roots. Move will not work across volumes.");

        CrossVolumeFolderMove.IsCrossVolumeFailure(exception, @"C:\Projects\Acme", @"E:\Trash\Acme").Should().BeTrue();
        CrossVolumeFolderMove.IsCrossVolumeFailure(exception, @"C:\Projects\Acme", @"C:\Trash\Acme").Should().BeFalse();
    }

    [TestCase(NotSameDeviceHResult)]
    [TestCase(CrossDeviceLinkErrno)]
    public void IsCrossVolumeFailure_RecognizesTheRenameFailure(int hResult)
    {
        var exception = new IOException("The system cannot move the file to a different disk drive.", hResult);

        var sourcePath = Path.Combine(_root, "source");
        var destPath = Path.Combine(_root, "dest");

        CrossVolumeFolderMove.IsCrossVolumeFailure(exception, sourcePath, destPath).Should().BeTrue();
    }

    [Test]
    public void IsCrossVolumeFailure_IgnoresOtherFailures()
    {
        var sourcePath = Path.Combine(_root, "source");
        var destPath = Path.Combine(_root, "dest");

        var sharingViolation = new IOException("The file is in use.", SharingViolationHResult);
        var accessDenied = new UnauthorizedAccessException("Access is denied.");

        CrossVolumeFolderMove.IsCrossVolumeFailure(sharingViolation, sourcePath, destPath).Should().BeFalse();
        CrossVolumeFolderMove.IsCrossVolumeFailure(accessDenied, sourcePath, destPath).Should().BeFalse();
    }

    [Test]
    public void Move_CopiesTheTreeAndDeletesTheSource()
    {
        var sourcePath = Path.Combine(_root, "source");
        Directory.CreateDirectory(Path.Combine(sourcePath, "nested", "empty"));
        File.WriteAllText(Path.Combine(sourcePath, "top.txt"), "top");
        File.WriteAllText(Path.Combine(sourcePath, "nested", "deep.txt"), "deep");
        var readOnlyPath = Path.Combine(sourcePath, "nested", "locked.txt");
        File.WriteAllText(readOnlyPath, "locked");
        File.SetAttributes(readOnlyPath, FileAttributes.ReadOnly);
        var destPath = Path.Combine(_root, "dest");

        var result = CrossVolumeFolderMove.Move(sourcePath, destPath, _logger);

        result.IsSuccess.Should().BeTrue(result.MessageChain);
        Directory.Exists(sourcePath).Should().BeFalse();
        File.ReadAllText(Path.Combine(destPath, "top.txt")).Should().Be("top");
        File.ReadAllText(Path.Combine(destPath, "nested", "deep.txt")).Should().Be("deep");
        File.ReadAllText(Path.Combine(destPath, "nested", "locked.txt")).Should().Be("locked");
        Directory.Exists(Path.Combine(destPath, "nested", "empty")).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(_root).Should().ContainSingle("the source leaves nothing beside it");
    }

    [Test]
    public void Move_RefusesATreeThatHoldsALink()
    {
        var targetPath = Path.Combine(_root, "target.txt");
        File.WriteAllText(targetPath, "target");
        var sourcePath = Path.Combine(_root, "source");
        Directory.CreateDirectory(Path.Combine(sourcePath, "nested"));
        File.WriteAllText(Path.Combine(sourcePath, "plain.txt"), "plain");
        var linkPath = Path.Combine(sourcePath, "nested", "link.txt");
        File.CreateSymbolicLink(linkPath, targetPath);
        var destPath = Path.Combine(_root, "dest");

        var result = CrossVolumeFolderMove.Move(sourcePath, destPath, _logger);

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain(linkPath);
        Directory.Exists(destPath).Should().BeFalse("nothing is copied from a tree that holds a link");
        File.Exists(Path.Combine(sourcePath, "plain.txt")).Should().BeTrue();
        new FileInfo(linkPath).LinkTarget.Should().Be(targetPath);
    }

    [Test]
    public void Move_RemovesThePartialCopyWhenACopyFails()
    {
        var sourcePath = Path.Combine(_root, "source");
        Directory.CreateDirectory(sourcePath);
        File.WriteAllText(Path.Combine(sourcePath, "a.txt"), "first");
        var unreadablePath = Path.Combine(sourcePath, "z.txt");
        File.WriteAllText(unreadablePath, "last");
        var destPath = Path.Combine(_root, "dest");

        // Windows refuses to read a file another handle holds without sharing. macOS and Linux have no
        // such lock, so the file is made unreadable instead.
        FileStream? lockHandle = null;
        if (OperatingSystem.IsWindows())
        {
            lockHandle = new FileStream(unreadablePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        else
        {
            File.SetUnixFileMode(unreadablePath, UnixFileMode.None);
        }

        Result result;
        try
        {
            result = CrossVolumeFolderMove.Move(sourcePath, destPath, _logger);
        }
        finally
        {
            lockHandle?.Dispose();
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(unreadablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        result.IsFailure.Should().BeTrue();
        Directory.Exists(destPath).Should().BeFalse();
        File.ReadAllText(Path.Combine(sourcePath, "a.txt")).Should().Be("first");
        File.ReadAllText(unreadablePath).Should().Be("last");
    }

    [Test]
    public void Move_SourceThatCannotBeMoved_RemovesTheCopyAndKeepsTheSourceWhole()
    {
        var parentPath = Path.Combine(_root, "parent");
        var sourcePath = Path.Combine(parentPath, "source");
        Directory.CreateDirectory(sourcePath);
        File.WriteAllText(Path.Combine(sourcePath, "a.txt"), "first");
        var openPath = Path.Combine(sourcePath, "z.txt");
        File.WriteAllText(openPath, "last");
        var destPath = Path.Combine(_root, "dest");

        // Windows refuses to rename a folder that holds a file another handle has open, though the copy can
        // still read it. macOS and Linux rename it anyway, so the parent folder is made read-only instead.
        FileStream? openHandle = null;
        if (OperatingSystem.IsWindows())
        {
            openHandle = new FileStream(openPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        else
        {
            File.SetUnixFileMode(parentPath, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        Result result;
        try
        {
            result = CrossVolumeFolderMove.Move(sourcePath, destPath, _logger);
        }
        finally
        {
            openHandle?.Dispose();
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(parentPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        result.IsFailure.Should().BeTrue();
        Directory.Exists(destPath).Should().BeFalse("the copy is removed when the source cannot be moved");
        File.ReadAllText(Path.Combine(sourcePath, "a.txt")).Should().Be("first");
        File.ReadAllText(openPath).Should().Be("last");
        Directory.EnumerateFileSystemEntries(parentPath).Should().ContainSingle("nothing is left beside the source");
    }
}
