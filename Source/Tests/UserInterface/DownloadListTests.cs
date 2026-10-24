using Celbridge.Downloads;
using Celbridge.UserInterface.Services;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Covers the rows of the downloads list. Rows are newest first, and the limit drops the oldest finished row
/// while keeping running rows. A succeeded row leaves once its file is missing.
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
    public void RemoveSucceeded_RemovesOnlyTheSucceededRowsOfTheFiles()
    {
        var missingFile = AddSucceeded("missing.pdf");
        var keptFile = AddSucceeded("kept.pdf");
        _rows.AddFailed("failed.pdf", "https://example.com/failed.pdf", "The transfer did not complete.");

        var isRemoved = _rows.RemoveSucceeded(new HashSet<ResourceKey> { missingFile, ResourceKey.Empty });

        // The missing files include the empty key, which is also the failed row's key. The failed row stays, since
        // only succeeded rows are removed.
        isRemoved.Should().BeTrue();
        _rows.Downloads.Select(download => download.FileName).Should().Equal("failed.pdf", "kept.pdf");
        _rows.GetSucceededFiles().Should().Equal(keptFile);
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
}
