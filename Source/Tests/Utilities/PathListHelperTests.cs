namespace Celbridge.Tests.Utilities;

/// <summary>
/// Tests for PathListHelper — prepending a folder to the separator-delimited lists used for PATH.
/// </summary>
[TestFixture]
public class PathListHelperTests
{
    private static readonly char Separator = System.IO.Path.PathSeparator;

    private static string List(params string[] entries) => string.Join(Separator, entries);

    [Test]
    public void TryPrepend_FolderAbsent_PutsItFirst()
    {
        PathListHelper.TryPrepend(List("/usr/bin", "/bin"), "/app/uv", out var result)
            .Should().BeTrue();

        result.Should().Be(List("/app/uv", "/usr/bin", "/bin"));
    }

    [Test]
    public void TryPrepend_FolderAlreadyPresent_LeavesListUnchanged()
    {
        var pathList = List("/usr/bin", "/app/uv", "/bin");

        PathListHelper.TryPrepend(pathList, "/app/uv", out var result)
            .Should().BeTrue();

        result.Should().Be(pathList);
    }

    [Test]
    public void TryPrepend_AppliedTwice_DoesNotDuplicateTheEntry()
    {
        PathListHelper.TryPrepend(List("/usr/bin"), "/app/uv", out var once);

        PathListHelper.TryPrepend(once, "/app/uv", out var twice);

        twice.Should().Be(once);
    }

    [Test]
    public void TryPrepend_EntryWithTheFolderAsAPrefix_StillPrepends()
    {
        // A whole-entry comparison, so /app/uv-extra does not stand in for /app/uv.
        PathListHelper.TryPrepend(List("/app/uv-extra"), "/app/uv", out var result)
            .Should().BeTrue();

        result.Should().Be(List("/app/uv", "/app/uv-extra"));
    }

    [Test]
    public void TryPrepend_EmptyList_ReturnsTheFolderAlone()
    {
        PathListHelper.TryPrepend(string.Empty, "/app/uv", out var result)
            .Should().BeTrue();

        result.Should().Be("/app/uv");
    }

    [Test]
    public void TryPrepend_NullList_ReturnsTheFolderAlone()
    {
        PathListHelper.TryPrepend(null, "/app/uv", out var result)
            .Should().BeTrue();

        result.Should().Be("/app/uv");
    }

    [Test]
    public void TryPrepend_FolderContainingTheSeparator_FailsAndKeepsTheList()
    {
        var pathList = List("/usr/bin");
        var folder = $"/work{Separator}2026/.celbridge/python/uv_bin";

        PathListHelper.TryPrepend(pathList, folder, out var result)
            .Should().BeFalse();

        result.Should().Be(pathList);
    }

    [Test]
    public void TryPrepend_EmptyFolder_FailsAndKeepsTheList()
    {
        var pathList = List("/usr/bin");

        PathListHelper.TryPrepend(pathList, string.Empty, out var result)
            .Should().BeFalse();

        result.Should().Be(pathList);
    }

    [Test]
    public void TryPrepend_CaseDifferingEntry_FollowsTheFileSystemComparison()
    {
        PathListHelper.TryPrepend(List("/APP/UV"), "/app/uv", out var result);

        var isCaseInsensitive = PathComparison.Comparison == StringComparison.OrdinalIgnoreCase;

        result.Should().Be(isCaseInsensitive
            ? List("/APP/UV")
            : List("/app/uv", "/APP/UV"));
    }
}
