using Celbridge.Resources;
using Celbridge.Spreadsheet;
using Celbridge.Spreadsheet.Commands;
using Celbridge.Workspace;
using ClosedXML.Excel;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for the ISpreadsheet*Command implementations against fixture .xlsx
/// workbooks generated in SetUp via ClosedXML. The workspace wrapper is a
/// stub that resolves a single ResourceKey to the fixture path so the
/// commands exercise their full code path including path resolution.
/// </summary>
[TestFixture]
public class SpreadsheetCommandTests
{
    private const string WorkbookResourceName = "data/test.xlsx";

    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private ResourceKey _workbookResource;
    private string _tempFolder = null!;
    private string _workbookPath = null!;

    [SetUp]
    public void SetUp()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(SpreadsheetCommandTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);

        _workbookPath = Path.Combine(_tempFolder, "test.xlsx");
        _workbookResource = new ResourceKey(WorkbookResourceName);

        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.ResolveResourcePath(_workbookResource).Returns(Result<string>.Ok(_workbookPath));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
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
    public async Task WriteCells_WritesValuesToExistingSheet()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Q1");
        });

        var command = new WriteCellsCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Q1",
            Edits = new[]
            {
                new SpreadsheetCellEdit("A1", "month"),
                new SpreadsheetCellEdit("B1", "total"),
                new SpreadsheetCellEdit("A2", "Jan"),
                new SpreadsheetCellEdit("B2", 100.0)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Q1");
        sheet.Cell("A1").GetString().Should().Be("month");
        sheet.Cell("B2").GetDouble().Should().Be(100.0);
    }

    [Test]
    public async Task WriteCells_WritesFormulaWhenIsFormulaTrue()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = 10;
            sheet.Cell("A2").Value = 20;
        });

        var command = new WriteCellsCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Q1",
            Edits = new[]
            {
                new SpreadsheetCellEdit("A3", "=SUM(A1:A2)", IsFormula: true)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        var cell = workbook.Worksheet("Q1").Cell("A3");
        cell.HasFormula.Should().BeTrue();
        cell.FormulaA1.Should().Be("SUM(A1:A2)");
    }

    [Test]
    public async Task WriteCells_FormulaStringWithoutIsFormula_IsWrittenAsText()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Q1");
        });

        var command = new WriteCellsCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Q1",
            Edits = new[]
            {
                new SpreadsheetCellEdit("A1", "=SUM(A2:A3)")
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        var cell = workbook.Worksheet("Q1").Cell("A1");
        cell.HasFormula.Should().BeFalse();
        cell.GetString().Should().Be("=SUM(A2:A3)");
    }

    [Test]
    public async Task WriteCells_NullValueClearsCell()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "existing";
        });

        var command = new WriteCellsCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Q1",
            Edits = new[]
            {
                new SpreadsheetCellEdit("A1", null)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheet("Q1").Cell("A1").IsEmpty().Should().BeTrue();
    }

    [Test]
    public async Task WriteCells_MissingSheet_ReturnsFailure()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Q1");
        });

        var command = new WriteCellsCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Missing",
            Edits = new[] { new SpreadsheetCellEdit("A1", 1.0) }
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Missing");
    }

    [Test]
    public async Task AppendRows_AppendsAfterUsedRange()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "month";
            sheet.Cell("B1").Value = "total";
            sheet.Cell("A2").Value = "Jan";
            sheet.Cell("B2").Value = 100;
        });

        var command = new AppendRowsCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Q1",
            Rows = new IReadOnlyList<object?>[]
            {
                new object?[] { "Feb", 200.0 },
                new object?[] { "Mar", 300.0 }
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.AppendedRowCount.Should().Be(2);
        command.ResultValue.FirstRow.Should().Be(3);
        command.ResultValue.LastRow.Should().Be(4);

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Q1");
        sheet.Cell("A3").GetString().Should().Be("Feb");
        sheet.Cell("B4").GetDouble().Should().Be(300.0);
    }

    [Test]
    public async Task AppendRows_EmptySheet_StartsAtRowOne()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Q1");
        });

        var command = new AppendRowsCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Q1",
            Rows = new IReadOnlyList<object?>[]
            {
                new object?[] { "month", "total" },
                new object?[] { "Jan", 100.0 }
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.FirstRow.Should().Be(1);
        command.ResultValue.LastRow.Should().Be(2);

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Q1");
        sheet.Cell("A1").GetString().Should().Be("month");
        sheet.Cell("B2").GetDouble().Should().Be(100.0);
    }

    [Test]
    public async Task ImportCsv_PopulatesExistingSheet()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Cell("A1").Value = "stale";
        });

        var csvText = "month,total\r\nJan,100\r\nFeb,200\r\n";

        var command = new ImportCsvCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Data",
            CsvText = csvText
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.RowCount.Should().Be(3);
        command.ResultValue.ColumnCount.Should().Be(2);
        command.ResultValue.SheetCreated.Should().BeFalse();

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Data");
        sheet.Cell("A1").GetString().Should().Be("month");
        sheet.Cell("A3").GetString().Should().Be("Feb");
    }

    [Test]
    public async Task ImportCsv_HandlesQuotedFieldsAndEmbeddedNewlines()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var csvText = "name,note\r\n\"Smith, John\",\"He said \"\"hi\"\"\"\r\n\"Multi\",\"line1\nline2\"\r\n";

        var command = new ImportCsvCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Data",
            CsvText = csvText
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Data");
        sheet.Cell("A2").GetString().Should().Be("Smith, John");
        sheet.Cell("B2").GetString().Should().Be("He said \"hi\"");
        sheet.Cell("B3").GetString().Should().Be("line1\nline2");
    }

    [Test]
    public async Task ImportCsv_MissingSheet_FailsWithoutCreateIfMissing()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Other");
        });

        var command = new ImportCsvCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Missing",
            CsvText = "a,b\r\n1,2\r\n",
            CreateIfMissing = false
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Missing");
    }

    [Test]
    public async Task ImportCsv_CreatesSheetWhenCreateIfMissingTrue()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Other");
        });

        var command = new ImportCsvCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "New",
            CsvText = "a,b\r\n1,2\r\n",
            CreateIfMissing = true
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.SheetCreated.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheets.Contains("New").Should().BeTrue();
    }

    [Test]
    public async Task AddSheet_AddsNewSheet()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Sheet1");
        });

        var command = new AddSheetCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Q2"
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheets.Contains("Q2").Should().BeTrue();
    }

    [Test]
    public async Task AddSheet_DuplicateName_ReturnsFailure()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Sheet1");
        });

        var command = new AddSheetCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Sheet1"
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("already exists");
    }

    [Test]
    public async Task RemoveSheet_RemovesNamedSheet()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Sheet1");
            workbook.Worksheets.Add("ToRemove");
        });

        var command = new RemoveSheetCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "ToRemove"
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheets.Contains("ToRemove").Should().BeFalse();
    }

    [Test]
    public async Task RemoveSheet_LastSheet_ReturnsFailure()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Only");
        });

        var command = new RemoveSheetCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Only"
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("at least one");
    }

    [Test]
    public async Task RenameSheet_RenamesNamedSheet()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("OldName");
        });

        var command = new RenameSheetCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "OldName",
            NewName = "NewName"
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheets.Contains("OldName").Should().BeFalse();
        workbook.Worksheets.Contains("NewName").Should().BeTrue();
    }

    [Test]
    public async Task RenameSheet_NameCollision_ReturnsFailure()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("A");
            workbook.Worksheets.Add("B");
        });

        var command = new RenameSheetCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "A",
            NewName = "B"
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("already exists");
    }

    [Test]
    public async Task WriteCells_FormulaIsRecalculatedOnSave()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Q1");
        });

        var writeCommand = new WriteCellsCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Q1",
            Edits = new[]
            {
                new SpreadsheetCellEdit("A1", 10.0),
                new SpreadsheetCellEdit("A2", 20.0),
                new SpreadsheetCellEdit("A3", "=SUM(A1:A2)", IsFormula: true)
            }
        };
        var result = await writeCommand.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        var formulaCell = workbook.Worksheet("Q1").Cell("A3");
        formulaCell.NeedsRecalculation.Should().BeFalse();
        formulaCell.CachedValue.GetNumber().Should().Be(30.0);
    }

    [Test]
    public async Task Commands_NonXlsxResource_ReturnFailure()
    {
        var nonXlsxResource = new ResourceKey("notes/readme.md");
        var nonXlsxPath = Path.Combine(_tempFolder, "readme.md");
        await File.WriteAllTextAsync(nonXlsxPath, "hello");
        _resourceRegistry.ResolveResourcePath(nonXlsxResource).Returns(Result<string>.Ok(nonXlsxPath));

        var command = new AddSheetCommand(_workspaceWrapper)
        {
            FileResource = nonXlsxResource,
            Sheet = "X"
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain(".xlsx");
    }

    private void CreateWorkbook(Action<XLWorkbook> populate)
    {
        using var workbook = new XLWorkbook();
        populate(workbook);
        workbook.SaveAs(_workbookPath);
    }
}
