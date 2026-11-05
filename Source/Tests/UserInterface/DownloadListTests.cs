using Celbridge.Downloads;
using Celbridge.UserInterface.Services;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Covers the rows of the downloads list. Rows are newest first, and the limit drops the oldest finished row
/// while keeping running rows. A succeeded row leaves once its file is missing. A row for a file saved outside the
/// project records the file's path, and its Resource is the empty key.
/// </summary>
[TestFixture]
public class DownloadListTests
{
    private DownloadList _rows = null!;

    [SetUp]
    public void Setup()
    {
        _rows = new DownloadList();
    }

    [Test]
    public void TheOldestFinishedRow_IsDroppedOnceTheListIsFull()
    {
        for (var index = 0; index < DownloadList.Limit + 5; index++)
        {
            AddSucceeded($"report{index}.pdf");
        }

        _rows.Downloads.Should().HaveCount(DownloadList.Limit);
        _rows.Downloads[0].FileName.Should().Be($"report{DownloadList.Limit + 4}.pdf");
    }

    [Test]
    public void ARowStillRunning_IsKeptOnceTheListIsFull()
    {
        // The row of a running download stays, so the user can follow and stop the transfer. With every row
        // running, the list grows past the limit.
        for (var index = 0; index < DownloadList.Limit + 5; index++)
        {
            _rows.AddRunning($"report{index}.pdf", "https://example.com/report.pdf");
        }

        _rows.Downloads.Should().HaveCount(DownloadList.Limit + 5);
        _rows.Downloads.Should().OnlyContain(download => download.Status == DownloadStatus.InProgress);
    }

    [Test]
    public void RemoveSucceededResources_RemovesOnlyTheSucceededRowsOfTheKeys()
    {
        var missingFile = AddSucceeded("missing.pdf");
        var keptFile = AddSucceeded("kept.pdf");
        _rows.AddFailed("failed.pdf", "https://example.com/failed.pdf", "The download could not complete.");

        var isRemoved = _rows.RemoveSucceededResources(new HashSet<ResourceKey> { missingFile, ResourceKey.Empty });

        // The missing files include the empty key, which is also the failed row's key. The failed row stays, since
        // only succeeded rows are removed.
        isRemoved.Should().BeTrue();
        _rows.Downloads.Select(download => download.FileName).Should().Equal("failed.pdf", "kept.pdf");
        _rows.GetSucceededResources().Should().Equal(keptFile);
    }

    [Test]
    public void ARowOutsideTheProject_IsListedByItsPath_AndNotByAResource()
    {
        var projectFile = AddSucceeded("report.pdf");
        var outsidePath = AddSucceededOutside("notes.txt");

        _rows.GetSucceededResources().Should().Equal(projectFile);
        _rows.GetSucceededFilePaths().Should().Equal(outsidePath);
    }

    [Test]
    public void RemoveSucceededFilePaths_RemovesTheRowOutsideTheProject()
    {
        AddSucceeded("report.pdf");
        var outsidePath = AddSucceededOutside("notes.txt");

        var isRemoved = _rows.RemoveSucceededFilePaths(new HashSet<string> { outsidePath });

        isRemoved.Should().BeTrue();
        _rows.Downloads.Select(download => download.FileName).Should().Equal("report.pdf");
        _rows.GetSucceededFilePaths().Should().BeEmpty();
    }

    [Test]
    public void RemoveSucceededResources_WithTheEmptyKey_KeepsTheRowOutsideTheProject()
    {
        AddSucceededOutside("notes.txt");

        var isRemoved = _rows.RemoveSucceededResources(new HashSet<ResourceKey> { ResourceKey.Empty });

        // Removal by resource key applies only to rows for files in the project. This row is for a file outside the
        // project, so its Resource is the empty key.
        isRemoved.Should().BeFalse();
        _rows.Downloads.Should().ContainSingle()
            .Which.FileName.Should().Be("notes.txt");
    }

    // Adds a running row, settles the row as succeeded, and returns the row's file.
    private ResourceKey AddSucceeded(string fileName)
    {
        var file = new ResourceKey($"downloads/{fileName}");
        var downloadId = _rows.AddRunning(fileName, $"https://example.com/{fileName}");
        _rows.Update(downloadId, entry => entry with
        {
            Resource = file,
            Status = DownloadStatus.Succeeded
        });

        return file;
    }

    // Adds a running row, settles the row as succeeded with a file saved outside the project, and returns the
    // file's path.
    private string AddSucceededOutside(string fileName)
    {
        var filePath = Path.Combine(Path.GetTempPath(), "elsewhere", fileName);
        var downloadId = _rows.AddRunning(fileName, $"https://example.com/{fileName}");
        _rows.Update(downloadId, entry => entry with
        {
            FilePath = filePath,
            Status = DownloadStatus.Succeeded
        });

        return filePath;
    }
}
