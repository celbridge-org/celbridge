using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Resources;
using Celbridge.Server;
using Celbridge.Spreadsheet;
using Celbridge.Tools;
using Celbridge.Workspace;
using ModelContextProtocol.Protocol;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for the SpreadsheetTools MCP read tool methods. Reader behaviour is
/// verified separately in SpreadsheetReaderTests; these tests focus on the
/// thin tool layer (input validation, resource resolution, JSON shape).
/// </summary>
[TestFixture]
public class SpreadsheetToolTests
{
    private IApplicationServiceProvider _services = null!;
    private ISpreadsheetReader _reader = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private ICommandService _commandService = null!;
    private string _tempFolder = null!;

    [SetUp]
    public void SetUp()
    {
        _services = Substitute.For<IApplicationServiceProvider>();
        _reader = Substitute.For<ISpreadsheetReader>();
        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _commandService = Substitute.For<ICommandService>();

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _services.GetRequiredService<IWorkspaceWrapper>().Returns(workspaceWrapper);
        _services.GetRequiredService<ISpreadsheetReader>().Returns(_reader);
        _services.GetRequiredService<ICommandService>().Returns(_commandService);

        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(SpreadsheetToolTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, true);
        }
    }

    [Test]
    public void GetContext_ReturnsSpreadsheetContextMarkdown()
    {
        var tools = new SpreadsheetTools(_services);
        var text = GetResultText(tools.GetContext());

        text.Should().Contain("# Celbridge Spreadsheet Tools");
        text.Should().Contain("A1 notation");
        text.Should().Contain("spreadsheet_recalculate");
    }

    [Test]
    public void GetInfo_DispatchesToReaderAndReturnsJson()
    {
        var workbookPath = CreatePlaceholderFile("data/sales.xlsx");
        var info = new SpreadsheetWorkbookInfo(
            new[] { new SpreadsheetSheetInfo("Q1", "A1:B2", 2, 2) },
            Array.Empty<SpreadsheetNamedRange>());
        _reader.GetInfo(workbookPath).Returns(Result<SpreadsheetWorkbookInfo>.Ok(info));

        var tools = new SpreadsheetTools(_services);
        var root = ParseResult(tools.GetInfo("data/sales.xlsx"));

        var sheets = root.GetProperty("sheets");
        sheets.GetArrayLength().Should().Be(1);
        sheets[0].GetProperty("name").GetString().Should().Be("Q1");
        sheets[0].GetProperty("usedRange").GetString().Should().Be("A1:B2");
    }

    [Test]
    public void GetInfo_NonXlsxResource_ReturnsError()
    {
        var tools = new SpreadsheetTools(_services);
        var result = tools.GetInfo("notes/readme.md");

        result.IsError.Should().BeTrue();
        GetResultText(result).Should().Contain(".xlsx");
    }

    [Test]
    public void GetInfo_MissingFile_ReturnsError()
    {
        var resource = new ResourceKey("data/missing.xlsx");
        var missingPath = Path.Combine(_tempFolder, "missing.xlsx");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(missingPath));

        var tools = new SpreadsheetTools(_services);
        var result = tools.GetInfo("data/missing.xlsx");

        result.IsError.Should().BeTrue();
        GetResultText(result).Should().Contain("File not found");
    }

    [Test]
    public void ReadSheet_DispatchesToReaderWithOptions()
    {
        var workbookPath = CreatePlaceholderFile("data/sales.xlsx");
        SpreadsheetReadOptions? capturedOptions = null;
        _reader.ReadSheet(workbookPath, "Q1", Arg.Do<SpreadsheetReadOptions>(o => capturedOptions = o))
            .Returns(Result<SpreadsheetReadResult>.Ok(
                new SpreadsheetReadResult(
                    new object?[] { new object?[] { "Jan", 100.0 } },
                    1,
                    Array.Empty<string>())));

        var tools = new SpreadsheetTools(_services);
        var root = ParseResult(tools.ReadSheet("data/sales.xlsx", "Q1", "A1:B2", "values", false, 0, 0));

        capturedOptions.Should().NotBeNull();
        capturedOptions!.Range.Should().Be("A1:B2");
        capturedOptions.Mode.Should().Be(SpreadsheetReadMode.Values);
        root.GetProperty("totalRowCount").GetInt32().Should().Be(1);
        var rows = root.GetProperty("rows");
        rows.GetArrayLength().Should().Be(1);
    }

    [Test]
    public void ReadSheet_FormulasMode_PassesFormulasModeThrough()
    {
        var workbookPath = CreatePlaceholderFile("data/sales.xlsx");
        SpreadsheetReadOptions? capturedOptions = null;
        _reader.ReadSheet(workbookPath, "Q1", Arg.Do<SpreadsheetReadOptions>(o => capturedOptions = o))
            .Returns(Result<SpreadsheetReadResult>.Ok(
                new SpreadsheetReadResult(Array.Empty<object?>(), 0, Array.Empty<string>())));

        var tools = new SpreadsheetTools(_services);
        tools.ReadSheet("data/sales.xlsx", "Q1", "", "formulas");

        capturedOptions!.Mode.Should().Be(SpreadsheetReadMode.Formulas);
    }

    [Test]
    public void ReadSheet_InvalidMode_ReturnsError()
    {
        var workbookPath = CreatePlaceholderFile("data/sales.xlsx");

        var tools = new SpreadsheetTools(_services);
        var result = tools.ReadSheet("data/sales.xlsx", "Q1", mode: "raw");

        result.IsError.Should().BeTrue();
        GetResultText(result).Should().Contain("mode");
    }

    [Test]
    public void ReadSheet_EmptySheetName_ReturnsError()
    {
        var workbookPath = CreatePlaceholderFile("data/sales.xlsx");

        var tools = new SpreadsheetTools(_services);
        var result = tools.ReadSheet("data/sales.xlsx", string.Empty);

        result.IsError.Should().BeTrue();
        GetResultText(result).Should().Contain("Sheet");
    }

    [Test]
    public async Task ToCsv_NoDestination_ReturnsCsvTextInline()
    {
        var workbookPath = CreatePlaceholderFile("data/sales.xlsx");
        var csv = "month,total\r\nJan,100\r\n";
        _reader.ToCsv(workbookPath, "Q1", null).Returns(
            Result<SpreadsheetCsvResult>.Ok(new SpreadsheetCsvResult(csv, 2, 2)));

        var tools = new SpreadsheetTools(_services);
        var result = await tools.ToCsv("data/sales.xlsx", "Q1");

        result.IsError.Should().NotBe(true);
        GetResultText(result).Should().Be(csv);
    }

    [Test]
    public async Task ToCsv_WithDestination_DispatchesWriteCommandAndReturnsSummary()
    {
        var workbookPath = CreatePlaceholderFile("data/sales.xlsx");
        var csv = "month,total\r\nJan,100\r\nFeb,200\r\n";
        _reader.ToCsv(workbookPath, "Q1", null).Returns(
            Result<SpreadsheetCsvResult>.Ok(new SpreadsheetCsvResult(csv, 3, 2)));

        IWriteFileCommand? capturedCommand = null;
        _commandService
            .ExecuteAsync<IWriteFileCommand>(
                Arg.Any<Action<IWriteFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<IWriteFileCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<IWriteFileCommand>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result.Ok());
            });

        var tools = new SpreadsheetTools(_services);
        var result = await tools.ToCsv("data/sales.xlsx", "Q1", range: "", destination: "exports/sales_q1.csv");

        result.IsError.Should().NotBe(true);
        capturedCommand.Should().NotBeNull();
        capturedCommand!.FileResource.Should().Be(new ResourceKey("exports/sales_q1.csv"));
        capturedCommand.Content.Should().Be(csv);

        var summary = GetResultText(result);
        summary.Should().Contain("3 rows");
        summary.Should().Contain("exports/sales_q1.csv");
    }

    [Test]
    public async Task ToCsv_WithInvalidDestinationKey_ReturnsError()
    {
        var workbookPath = CreatePlaceholderFile("data/sales.xlsx");
        _reader.ToCsv(workbookPath, "Q1", null).Returns(
            Result<SpreadsheetCsvResult>.Ok(new SpreadsheetCsvResult("a\r\n", 1, 1)));

        var tools = new SpreadsheetTools(_services);
        var result = await tools.ToCsv("data/sales.xlsx", "Q1", range: "", destination: "../escape.csv");

        result.IsError.Should().BeTrue();
        GetResultText(result).Should().Contain("destination");
    }

    private string CreatePlaceholderFile(string resourceKey)
    {
        var resource = new ResourceKey(resourceKey);
        var path = Path.Combine(_tempFolder, Path.GetFileName(resourceKey));
        File.WriteAllText(path, string.Empty);
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));
        return path;
    }

    private static string GetResultText(CallToolResult result)
    {
        return result.Content.OfType<TextContentBlock>().Single().Text;
    }

    private static JsonElement ParseResult(CallToolResult result)
    {
        var json = result.Content.OfType<TextContentBlock>().Single().Text;
        return JsonDocument.Parse(json).RootElement;
    }
}
