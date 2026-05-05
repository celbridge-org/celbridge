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
    }

    [Test]
    public void GetInfo_DispatchesToReaderAndReturnsJson()
    {
        var workbookPath = CreatePlaceholderFile("data/sales.xlsx");
        var info = new SpreadsheetWorkbookInfo(
            new[] { new SpreadsheetSheetInfo("Q1", 1, "A1:B2", 2, 2, 0, 0) },
            Array.Empty<SpreadsheetNamedRange>());
        _reader.GetInfo(workbookPath).Returns(Result<SpreadsheetWorkbookInfo>.Ok(info));

        var tools = new SpreadsheetTools(_services);
        var root = ParseResult(tools.GetInfo("data/sales.xlsx"));

        var sheets = root.GetProperty("sheets");
        sheets.GetArrayLength().Should().Be(1);
        sheets[0].GetProperty("name").GetString().Should().Be("Q1");
        sheets[0].GetProperty("position").GetInt32().Should().Be(1);
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
    public async Task ExportCsv_NoDestination_ReturnsCsvTextInline()
    {
        var workbookPath = CreatePlaceholderFile("data/sales.xlsx");
        var csv = "month,total\r\nJan,100\r\n";
        _reader.ExportCsv(workbookPath, "Q1", null).Returns(
            Result<SpreadsheetExportCsvResult>.Ok(new SpreadsheetExportCsvResult(csv, 2, 2)));

        var tools = new SpreadsheetTools(_services);
        var result = await tools.ExportCsv("data/sales.xlsx", "Q1");

        result.IsError.Should().NotBe(true);
        GetResultText(result).Should().Be(csv);
    }

    [Test]
    public async Task ExportCsv_WithDestination_DispatchesWriteCommandAndReturnsJsonMetadata()
    {
        var workbookPath = CreatePlaceholderFile("data/sales.xlsx");
        var csv = "month,total\r\nJan,100\r\nFeb,200\r\n";
        _reader.ExportCsv(workbookPath, "Q1", null).Returns(
            Result<SpreadsheetExportCsvResult>.Ok(new SpreadsheetExportCsvResult(csv, 3, 2)));

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
        var result = await tools.ExportCsv("data/sales.xlsx", "Q1", range: "", destination: "exports/sales_q1.csv");

        result.IsError.Should().NotBe(true);
        capturedCommand.Should().NotBeNull();
        capturedCommand!.FileResource.Should().Be(new ResourceKey("exports/sales_q1.csv"));
        capturedCommand.Content.Should().Be(csv);

        var root = ParseResult(result);
        root.GetProperty("rowCount").GetInt32().Should().Be(3);
        root.GetProperty("columnCount").GetInt32().Should().Be(2);
        root.GetProperty("byteCount").GetInt32().Should().Be(System.Text.Encoding.UTF8.GetByteCount(csv));
        root.GetProperty("destination").GetString().Should().Be("exports/sales_q1.csv");
    }

    [Test]
    public async Task ExportCsv_WithInvalidDestinationKey_ReturnsError()
    {
        var workbookPath = CreatePlaceholderFile("data/sales.xlsx");
        _reader.ExportCsv(workbookPath, "Q1", null).Returns(
            Result<SpreadsheetExportCsvResult>.Ok(new SpreadsheetExportCsvResult("a\r\n", 1, 1)));

        var tools = new SpreadsheetTools(_services);
        var result = await tools.ExportCsv("data/sales.xlsx", "Q1", range: "", destination: "../escape.csv");

        result.IsError.Should().BeTrue();
        GetResultText(result).Should().Contain("destination");
    }

    [Test]
    public async Task WriteCells_DispatchesCommandWithParsedEdits()
    {
        CreatePlaceholderFile("data/sales.xlsx");

        ISpreadsheetWriteCellsCommand? capturedCommand = null;
        _commandService
            .ExecuteAsync<ISpreadsheetWriteCellsCommand>(
                Arg.Any<Action<ISpreadsheetWriteCellsCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<ISpreadsheetWriteCellsCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<ISpreadsheetWriteCellsCommand>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result.Ok());
            });

        var editsJson = "[{\"cell\": \"A1\", \"value\": 42}, {\"cell\": \"B2\", \"value\": \"hi\"}, {\"cell\": \"C3\", \"value\": \"=SUM(A1:A2)\", \"isFormula\": true}]";

        var tools = new SpreadsheetTools(_services);
        var root = ParseResult(await tools.WriteCells("data/sales.xlsx", "Q1", editsJson));

        capturedCommand.Should().NotBeNull();
        capturedCommand!.Sheet.Should().Be("Q1");
        capturedCommand.Edits.Should().HaveCount(3);
        capturedCommand.Edits[0].Cell.Should().Be("A1");
        capturedCommand.Edits[0].Value.Should().Be(42.0);
        capturedCommand.Edits[2].IsFormula.Should().BeTrue();

        root.GetProperty("cellCount").GetInt32().Should().Be(3);
    }

    [Test]
    public async Task WriteCells_InvalidJson_ReturnsError()
    {
        CreatePlaceholderFile("data/sales.xlsx");

        var tools = new SpreadsheetTools(_services);
        var result = await tools.WriteCells("data/sales.xlsx", "Q1", "not json");

        result.IsError.Should().BeTrue();
        GetResultText(result).Should().Contain("Invalid edits JSON");
    }

    [Test]
    public async Task WriteCells_NonArrayEditsJson_ReturnsError()
    {
        CreatePlaceholderFile("data/sales.xlsx");

        var tools = new SpreadsheetTools(_services);
        var result = await tools.WriteCells("data/sales.xlsx", "Q1", "{\"cell\": \"A1\"}");

        result.IsError.Should().BeTrue();
        GetResultText(result).Should().Contain("array");
    }

    [Test]
    public async Task AppendRows_DispatchesCommandAndReturnsAckMetadata()
    {
        CreatePlaceholderFile("data/sales.xlsx");

        ISpreadsheetAppendRowsCommand? capturedCommand = null;
        _commandService
            .ExecuteAsync<ISpreadsheetAppendRowsCommand, SpreadsheetAppendRowsResult>(
                Arg.Any<Action<ISpreadsheetAppendRowsCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<ISpreadsheetAppendRowsCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<ISpreadsheetAppendRowsCommand>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result<SpreadsheetAppendRowsResult>.Ok(
                    new SpreadsheetAppendRowsResult(2, 5, 6)));
            });

        var rowsJson = "[[\"Mar\", 1200], [\"Apr\", 1450]]";

        var tools = new SpreadsheetTools(_services);
        var root = ParseResult(await tools.AppendRows("data/sales.xlsx", "Q1", rowsJson));

        capturedCommand.Should().NotBeNull();
        capturedCommand!.Rows.Should().HaveCount(2);
        capturedCommand.Rows[0].Should().Equal("Mar", 1200.0);

        root.GetProperty("appendedRowCount").GetInt32().Should().Be(2);
        root.GetProperty("firstRow").GetInt32().Should().Be(5);
        root.GetProperty("lastRow").GetInt32().Should().Be(6);
    }

    [Test]
    public async Task ImportCsv_DispatchesCommandWithImports()
    {
        CreatePlaceholderFile("data/sales.xlsx");

        ISpreadsheetImportCsvCommand? capturedCommand = null;
        _commandService
            .ExecuteAsync<ISpreadsheetImportCsvCommand, SpreadsheetImportCsvResult>(
                Arg.Any<Action<ISpreadsheetImportCsvCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<ISpreadsheetImportCsvCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<ISpreadsheetImportCsvCommand>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result<SpreadsheetImportCsvResult>.Ok(
                    new SpreadsheetImportCsvResult(2, 5, 1)));
            });

        var importsJson = "[{\"sheet\": \"Q2\", \"csvText\": \"a,b\\r\\n1,2\\r\\n\", \"createIfMissing\": true}, {\"sheet\": \"Q3\", \"csvText\": \"c,d\\r\\ne,f\\r\\ng,h\\r\\n\"}]";

        var tools = new SpreadsheetTools(_services);
        var root = ParseResult(await tools.ImportCsv("data/sales.xlsx", importsJson));

        capturedCommand.Should().NotBeNull();
        capturedCommand!.Imports.Should().HaveCount(2);
        capturedCommand.Imports[0].Sheet.Should().Be("Q2");
        capturedCommand.Imports[0].CsvText.Should().Be("a,b\r\n1,2\r\n");
        capturedCommand.Imports[0].CreateIfMissing.Should().BeTrue();
        capturedCommand.Imports[1].Sheet.Should().Be("Q3");

        root.GetProperty("importsApplied").GetInt32().Should().Be(2);
        root.GetProperty("totalRowCount").GetInt32().Should().Be(5);
        root.GetProperty("sheetsCreated").GetInt32().Should().Be(1);
    }

    [Test]
    public async Task AddSheets_DispatchesCommandAndReturnsSheetNames()
    {
        CreatePlaceholderFile("data/sales.xlsx");

        ISpreadsheetAddSheetsCommand? capturedCommand = null;
        _commandService
            .ExecuteAsync<ISpreadsheetAddSheetsCommand, SpreadsheetAddSheetsResult>(
                Arg.Any<Action<ISpreadsheetAddSheetsCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<ISpreadsheetAddSheetsCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<ISpreadsheetAddSheetsCommand>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result<SpreadsheetAddSheetsResult>.Ok(
                    new SpreadsheetAddSheetsResult(new[] { "Q2", "Q3", "Q4" })));
            });

        var tools = new SpreadsheetTools(_services);
        var root = ParseResult(await tools.AddSheets("data/sales.xlsx", "[\"Q2\", \"Q3\", \"Q4\"]"));

        capturedCommand.Should().NotBeNull();
        capturedCommand!.Sheets.Should().Equal("Q2", "Q3", "Q4");

        var sheets = root.GetProperty("sheets");
        sheets.GetArrayLength().Should().Be(3);
        sheets[0].GetString().Should().Be("Q2");
        sheets[2].GetString().Should().Be("Q4");
    }

    [Test]
    public async Task RemoveSheet_DispatchesCommandAndReturnsSheetName()
    {
        CreatePlaceholderFile("data/sales.xlsx");

        ISpreadsheetRemoveSheetCommand? capturedCommand = null;
        _commandService
            .ExecuteAsync<ISpreadsheetRemoveSheetCommand>(
                Arg.Any<Action<ISpreadsheetRemoveSheetCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<ISpreadsheetRemoveSheetCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<ISpreadsheetRemoveSheetCommand>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result.Ok());
            });

        var tools = new SpreadsheetTools(_services);
        var root = ParseResult(await tools.RemoveSheet("data/sales.xlsx", "Q3"));

        capturedCommand.Should().NotBeNull();
        capturedCommand!.Sheet.Should().Be("Q3");
        root.GetProperty("sheet").GetString().Should().Be("Q3");
    }

    [Test]
    public async Task RenameSheet_DispatchesCommandAndReturnsBothNames()
    {
        CreatePlaceholderFile("data/sales.xlsx");

        ISpreadsheetRenameSheetCommand? capturedCommand = null;
        _commandService
            .ExecuteAsync<ISpreadsheetRenameSheetCommand>(
                Arg.Any<Action<ISpreadsheetRenameSheetCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<ISpreadsheetRenameSheetCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<ISpreadsheetRenameSheetCommand>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result.Ok());
            });

        var tools = new SpreadsheetTools(_services);
        var root = ParseResult(await tools.RenameSheet("data/sales.xlsx", "Old", "New"));

        capturedCommand.Should().NotBeNull();
        capturedCommand!.Sheet.Should().Be("Old");
        capturedCommand.NewName.Should().Be("New");
        root.GetProperty("previousName").GetString().Should().Be("Old");
        root.GetProperty("newName").GetString().Should().Be("New");
    }

    [Test]
    public async Task MoveSheet_DispatchesCommandAndReturnsPosition()
    {
        CreatePlaceholderFile("data/sales.xlsx");

        ISpreadsheetMoveSheetCommand? capturedCommand = null;
        _commandService
            .ExecuteAsync<ISpreadsheetMoveSheetCommand>(
                Arg.Any<Action<ISpreadsheetMoveSheetCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<ISpreadsheetMoveSheetCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<ISpreadsheetMoveSheetCommand>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result.Ok());
            });

        var tools = new SpreadsheetTools(_services);
        var root = ParseResult(await tools.MoveSheet("data/sales.xlsx", "Q3", 2));

        capturedCommand.Should().NotBeNull();
        capturedCommand!.Sheet.Should().Be("Q3");
        capturedCommand.Position.Should().Be(2);
        root.GetProperty("sheet").GetString().Should().Be("Q3");
        root.GetProperty("position").GetInt32().Should().Be(2);
    }

    [Test]
    public async Task MoveSheet_PositionBelowOne_ReturnsErrorWithoutDispatch()
    {
        CreatePlaceholderFile("data/sales.xlsx");

        var tools = new SpreadsheetTools(_services);
        var result = await tools.MoveSheet("data/sales.xlsx", "Q3", 0);

        result.IsError.Should().BeTrue();
    }

    [Test]
    public async Task SetActiveView_DispatchesCommandWithAllFields()
    {
        CreatePlaceholderFile("data/sales.xlsx");

        ISpreadsheetSetActiveViewCommand? capturedCommand = null;
        _commandService
            .ExecuteAsync<ISpreadsheetSetActiveViewCommand>(
                Arg.Any<Action<ISpreadsheetSetActiveViewCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<ISpreadsheetSetActiveViewCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<ISpreadsheetSetActiveViewCommand>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result.Ok());
            });

        var tools = new SpreadsheetTools(_services);
        var root = ParseResult(await tools.SetActiveView(
            "data/sales.xlsx",
            "Summary",
            range: "B50:F50",
            activeCell: "D50",
            topLeftCell: "A30"));

        capturedCommand.Should().NotBeNull();
        capturedCommand!.Sheet.Should().Be("Summary");
        capturedCommand.Range.Should().Be("B50:F50");
        capturedCommand.ActiveCell.Should().Be("D50");
        capturedCommand.TopLeftCell.Should().Be("A30");
        root.GetProperty("sheet").GetString().Should().Be("Summary");
        root.GetProperty("range").GetString().Should().Be("B50:F50");
        root.GetProperty("activeCell").GetString().Should().Be("D50");
        root.GetProperty("topLeftCell").GetString().Should().Be("A30");
    }

    [Test]
    public async Task SetActiveView_MissingSheetArg_ReturnsErrorWithoutDispatch()
    {
        CreatePlaceholderFile("data/sales.xlsx");

        var tools = new SpreadsheetTools(_services);
        var result = await tools.SetActiveView("data/sales.xlsx", string.Empty);

        result.IsError.Should().BeTrue();
    }

    [Test]
    public async Task FormatRanges_DispatchesCommandAndReturnsAckMetadata()
    {
        CreatePlaceholderFile("data/sales.xlsx");

        ISpreadsheetFormatRangesCommand? capturedCommand = null;
        _commandService
            .ExecuteAsync<ISpreadsheetFormatRangesCommand, SpreadsheetFormatRangesResult>(
                Arg.Any<Action<ISpreadsheetFormatRangesCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<ISpreadsheetFormatRangesCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<ISpreadsheetFormatRangesCommand>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result<SpreadsheetFormatRangesResult>.Ok(
                    new SpreadsheetFormatRangesResult(2, 5, false)));
            });

        var editsJson = "[{\"sheet\": \"Data\", \"range\": \"A1:C1\", \"format\": {\"textFormat\": {\"bold\": true}, \"backgroundColor\": \"#FFFF00\"}}, {\"sheet\": \"Other\", \"range\": \"B2\", \"format\": {\"horizontalAlignment\": \"CENTER\"}}]";

        var tools = new SpreadsheetTools(_services);
        var root = ParseResult(await tools.FormatRanges("data/sales.xlsx", editsJson));

        capturedCommand.Should().NotBeNull();
        capturedCommand!.Edits.Should().HaveCount(2);
        capturedCommand.Edits[0].Sheet.Should().Be("Data");
        capturedCommand.Edits[0].Range.Should().Be("A1:C1");
        capturedCommand.Edits[0].Format.TextFormat!.Bold.Should().BeTrue();
        capturedCommand.Edits[0].Format.BackgroundColor.Should().Be("#FFFF00");
        capturedCommand.Edits[1].Sheet.Should().Be("Other");
        capturedCommand.Edits[1].Format.HorizontalAlignment.Should().Be("CENTER");

        root.GetProperty("editsApplied").GetInt32().Should().Be(2);
        root.GetProperty("propertiesApplied").GetInt32().Should().Be(5);
        root.GetProperty("autoFitApplied").GetBoolean().Should().BeFalse();
    }

    [Test]
    public async Task FormatRanges_InvalidEditsJson_ReturnsError()
    {
        CreatePlaceholderFile("data/sales.xlsx");

        var tools = new SpreadsheetTools(_services);
        var result = await tools.FormatRanges("data/sales.xlsx", "not json");

        result.IsError.Should().BeTrue();
        GetResultText(result).Should().Contain("Invalid edits JSON");
    }

    [Test]
    public async Task FormatRanges_EmptyEditsJson_ReturnsError()
    {
        CreatePlaceholderFile("data/sales.xlsx");

        var tools = new SpreadsheetTools(_services);
        var result = await tools.FormatRanges("data/sales.xlsx", editsJson: "");

        result.IsError.Should().BeTrue();
        GetResultText(result).Should().Contain("Edits JSON");
    }

    [Test]
    public async Task FormatRanges_EmptyArray_ReturnsError()
    {
        CreatePlaceholderFile("data/sales.xlsx");

        var tools = new SpreadsheetTools(_services);
        var result = await tools.FormatRanges("data/sales.xlsx", editsJson: "[]");

        result.IsError.Should().BeTrue();
        GetResultText(result).Should().Contain("at least one");
    }

    [Test]
    public async Task FreezePanes_DispatchesCommandAndReturnsAckMetadata()
    {
        CreatePlaceholderFile("data/sales.xlsx");

        ISpreadsheetFreezePanesCommand? capturedCommand = null;
        _commandService
            .ExecuteAsync<ISpreadsheetFreezePanesCommand, SpreadsheetFreezePanesResult>(
                Arg.Any<Action<ISpreadsheetFreezePanesCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<ISpreadsheetFreezePanesCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<ISpreadsheetFreezePanesCommand>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result<SpreadsheetFreezePanesResult>.Ok(
                    new SpreadsheetFreezePanesResult("Q1", 1, 2)));
            });

        var tools = new SpreadsheetTools(_services);
        var root = ParseResult(await tools.FreezePanes("data/sales.xlsx", "Q1", rows: 1, columns: 2));

        capturedCommand.Should().NotBeNull();
        capturedCommand!.Sheet.Should().Be("Q1");
        capturedCommand.Rows.Should().Be(1);
        capturedCommand.Columns.Should().Be(2);

        root.GetProperty("sheet").GetString().Should().Be("Q1");
        root.GetProperty("rows").GetInt32().Should().Be(1);
        root.GetProperty("columns").GetInt32().Should().Be(2);
    }

    [Test]
    public async Task FreezePanes_NegativeRows_ReturnsError()
    {
        CreatePlaceholderFile("data/sales.xlsx");

        var tools = new SpreadsheetTools(_services);
        var result = await tools.FreezePanes("data/sales.xlsx", "Q1", rows: -1);

        result.IsError.Should().BeTrue();
        GetResultText(result).Should().Contain("non-negative");
    }

    [Test]
    public void ReadFormat_DispatchesToReaderAndReturnsGrid()
    {
        CreatePlaceholderFile("data/styles.xlsx");

        var formatResult = new SpreadsheetReadFormatResult(
            "Data!A1:B1",
            new List<List<SpreadsheetFormatSpec>>
            {
                new List<SpreadsheetFormatSpec>
                {
                    new SpreadsheetFormatSpec(TextFormat: new SpreadsheetTextFormat(Bold: true)),
                    new SpreadsheetFormatSpec(BackgroundColor: "#FFFF00")
                }
            });

        _reader.ReadFormat(Arg.Any<string>(), "Data", "A1:B1")
            .Returns(Result<SpreadsheetReadFormatResult>.Ok(formatResult));

        var tools = new SpreadsheetTools(_services);
        var root = ParseResult(tools.ReadFormat("data/styles.xlsx", "Data", "A1:B1"));

        root.GetProperty("range").GetString().Should().Be("Data!A1:B1");
        var rows = root.GetProperty("rows");
        rows.GetArrayLength().Should().Be(1);
        var firstRow = rows[0];
        firstRow.GetArrayLength().Should().Be(2);
        firstRow[0].GetProperty("textFormat").GetProperty("bold").GetBoolean().Should().BeTrue();
        firstRow[1].GetProperty("backgroundColor").GetString().Should().Be("#FFFF00");
    }

    [Test]
    public void ReadFormat_EmptySheetName_ReturnsError()
    {
        CreatePlaceholderFile("data/styles.xlsx");

        var tools = new SpreadsheetTools(_services);
        var result = tools.ReadFormat("data/styles.xlsx", sheet: "", range: "");

        result.IsError.Should().BeTrue();
        GetResultText(result).Should().Contain("Sheet");
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
