using Celbridge.Documents.Views;
using Celbridge.Host;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Tests.FileSystem;
using Celbridge.Utilities;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Tests for the report half of the editor bridge. A contribution names its own report id, so what
/// the host refuses on the way in is the whole of what stops one report damaging another.
/// </summary>
[TestFixture]
public class CustomDocumentHandlerTests
{
    private const string ConvertReportJson = """
        {
          "version": 1,
          "id": "acme-tiles-convert",
          "title": "Convert Tilesets",
          "generatedAt": "2026-08-18T09:00:00Z",
          "severity": "warning",
          "summary": "1 tileset could not be converted.",
          "sections": [
            {
              "title": "Tilesets",
              "kind": "findings",
              "severity": "warning",
              "items": [
                {
                  "severity": "warning",
                  "message": "Could not be converted.",
                  "resource": "project:tiles/town.tsx",
                  "detail": "unsupported bit depth",
                  "actions": [
                    { "kind": "openResource", "label": "Open", "resource": "project:tiles/town.tsx" }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private string _projectFolderPath = null!;
    private CustomDocumentHandler _handler = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(CustomDocumentHandlerTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        var project = Substitute.For<IProject>();
        project.ProjectFilePath.Returns(Path.Combine(_projectFolderPath, "Project.celbridge"));
        project.ProjectDataFolderPath.Returns(Path.Combine(_projectFolderPath, ".celbridge"));

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        var reportWriter = new ReportWriter(
            TestFileSystem.CreateLocal(),
            Substitute.For<ILogger<ReportWriter>>());

        _handler = new CustomDocumentHandler(
            null!,
            Substitute.For<ILogger<CustomDocumentHandlerTests>>(),
            projectService,
            reportWriter,
            () => throw new NotSupportedException(),
            () => throw new NotSupportedException());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolderPath))
        {
            try
            {
                Directory.Delete(_projectFolderPath, true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    [Test]
    public async Task AWrittenReport_LandsInTheReportsFolderAtItsId()
    {
        var result = await _handler.WriteReportAsync(ConvertReportJson);

        result.Resource.Should().Be("logs:reports/acme-tiles-convert.report");

        var reportPath = Path.Combine(
            ReportLocation.ResolveFolderPath(Path.Combine(_projectFolderPath, ".celbridge")),
            "acme-tiles-convert.report");

        File.Exists(reportPath).Should().BeTrue();
        File.ReadAllText(reportPath).Should().Contain("unsupported bit depth");
    }

    [Test]
    public async Task AReportIdThatEscapesTheFolder_IsRefused()
    {
        // The id becomes a file name and the glob that prunes its history, so it is checked before
        // anything reaches the disk.
        var escapingJson = ConvertReportJson.Replace("acme-tiles-convert", "../../../project-load");

        var act = async () => await _handler.WriteReportAsync(escapingJson);

        await act.Should().ThrowAsync<ArgumentException>();

        Directory.Exists(ReportLocation.ResolveFolderPath(Path.Combine(_projectFolderPath, ".celbridge")))
            .Should().BeFalse();
    }

    [Test]
    public async Task AReportThatIsNotAReport_IsRefused()
    {
        var act = async () => await _handler.WriteReportAsync("{ not json");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Test]
    public async Task AnUnknownActionKind_IsRefused()
    {
        // openResource is the only kind, and a report can be shared, so a newer one is not tolerated
        // on the way in even though the editor renders an unknown one harmlessly.
        var unknownActionJson = ConvertReportJson.Replace("openResource", "openUrl");

        var act = async () => await _handler.WriteReportAsync(unknownActionJson);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
