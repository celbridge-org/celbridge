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
            Imports = new[]
            {
                new SpreadsheetCsvImport("Data", csvText)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.ImportsApplied.Should().Be(1);
        command.ResultValue.TotalRowCount.Should().Be(3);
        command.ResultValue.SheetsCreated.Should().Be(0);

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
            Imports = new[]
            {
                new SpreadsheetCsvImport("Data", csvText)
            }
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
            Imports = new[]
            {
                new SpreadsheetCsvImport("Missing", "a,b\r\n1,2\r\n", CreateIfMissing: false)
            }
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
            Imports = new[]
            {
                new SpreadsheetCsvImport("New", "a,b\r\n1,2\r\n", CreateIfMissing: true)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.SheetsCreated.Should().Be(1);

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

        var command = new AddSheetsCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheets = new[] { "Q2" }
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

        var command = new AddSheetsCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheets = new[] { "Sheet1" }
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
    public async Task MoveSheet_ToFirstPosition_ReordersTabs()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("A");
            workbook.Worksheets.Add("B");
            workbook.Worksheets.Add("C");
        });

        var command = new MoveSheetCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "C",
            Position = 1
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheet("C").Position.Should().Be(1);
        workbook.Worksheet("A").Position.Should().Be(2);
        workbook.Worksheet("B").Position.Should().Be(3);
    }

    [Test]
    public async Task MoveSheet_AlreadyAtPosition_IsNoOpAndSucceeds()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("A");
            workbook.Worksheets.Add("B");
        });

        var command = new MoveSheetCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "A",
            Position = 1
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheet("A").Position.Should().Be(1);
        workbook.Worksheet("B").Position.Should().Be(2);
    }

    [Test]
    public async Task MoveSheet_MissingSheet_ReturnsFailure()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("A");
        });

        var command = new MoveSheetCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Nope",
            Position = 1
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("not found");
    }

    [Test]
    public async Task MoveSheet_PositionOutOfRange_ReturnsFailure()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("A");
            workbook.Worksheets.Add("B");
        });

        var command = new MoveSheetCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "A",
            Position = 5
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("out of range");
    }

    [Test]
    public async Task MoveSheet_PositionBelowOne_ReturnsFailure()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("A");
        });

        var command = new MoveSheetCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "A",
            Position = 0
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("1 or greater");
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

        var command = new AddSheetsCommand(_workspaceWrapper)
        {
            FileResource = nonXlsxResource,
            Sheets = new[] { "X" }
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain(".xlsx");
    }

    [Test]
    public async Task FormatRange_AppliesTextFormat_ToCellRange()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Cell("A1").Value = "Header";
            sheet.Cell("B1").Value = "Value";
        });

        var format = new SpreadsheetFormatSpec(
            TextFormat: new SpreadsheetTextFormat(Bold: true, Italic: true, FontSize: 14));

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "A1:B1", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.EditsApplied.Should().Be(1);
        command.ResultValue.PropertiesApplied.Should().Be(1);
        command.ResultValue.AutoFitApplied.Should().BeFalse();

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Data");
        sheet.Cell("A1").Style.Font.Bold.Should().BeTrue();
        sheet.Cell("A1").Style.Font.Italic.Should().BeTrue();
        sheet.Cell("A1").Style.Font.FontSize.Should().Be(14);
        sheet.Cell("B1").Style.Font.Bold.Should().BeTrue();
    }

    [Test]
    public async Task FormatRange_AppliesBackgroundColor_ToCellRange()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var format = new SpreadsheetFormatSpec(BackgroundColor: "#FFFF00");

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "A1:C1", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.PropertiesApplied.Should().Be(1);

        using var workbook = new XLWorkbook(_workbookPath);
        var cell = workbook.Worksheet("Data").Cell("A1");
        cell.Style.Fill.BackgroundColor.Should().Be(ClosedXML.Excel.XLColor.FromHtml("#FFFF00"));
    }

    [Test]
    public async Task FormatRange_AppliesBordersPerSide_ToCellRange()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var format = new SpreadsheetFormatSpec(
            Borders: new SpreadsheetBordersSpec(
                Top: new SpreadsheetBorderSide(Style: "SOLID", Color: "#000000"),
                Bottom: new SpreadsheetBorderSide(Style: "DASHED"),
                Left: new SpreadsheetBorderSide(Style: "NONE"),
                Right: new SpreadsheetBorderSide(Style: "DOTTED")));

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "B2", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.PropertiesApplied.Should().Be(1);

        using var workbook = new XLWorkbook(_workbookPath);
        var style = workbook.Worksheet("Data").Cell("B2").Style;
        style.Border.TopBorder.Should().Be(ClosedXML.Excel.XLBorderStyleValues.Thin);
        style.Border.BottomBorder.Should().Be(ClosedXML.Excel.XLBorderStyleValues.Dashed);
        style.Border.LeftBorder.Should().Be(ClosedXML.Excel.XLBorderStyleValues.None);
        style.Border.RightBorder.Should().Be(ClosedXML.Excel.XLBorderStyleValues.Dotted);
    }

    [Test]
    public async Task FormatRange_AppliesAlignment_ToCellRange()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var format = new SpreadsheetFormatSpec(
            HorizontalAlignment: "CENTER",
            VerticalAlignment: "MIDDLE",
            WrapText: true);

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "A1", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.PropertiesApplied.Should().Be(3);

        using var workbook = new XLWorkbook(_workbookPath);
        var style = workbook.Worksheet("Data").Cell("A1").Style;
        style.Alignment.Horizontal.Should().Be(ClosedXML.Excel.XLAlignmentHorizontalValues.Center);
        style.Alignment.Vertical.Should().Be(ClosedXML.Excel.XLAlignmentVerticalValues.Center);
        style.Alignment.WrapText.Should().BeTrue();
    }

    [Test]
    public async Task FormatRange_AppliesNumberFormat_ToCellRange()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Cell("B2").Value = 1234.5;
        });

        var format = new SpreadsheetFormatSpec(NumberFormat: "#,##0.00");

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "B2", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.PropertiesApplied.Should().Be(1);

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheet("Data").Cell("B2").Style.NumberFormat.Format.Should().Be("#,##0.00");
    }

    [Test]
    public async Task FormatRange_AppliesColumnWidth_ToColumnRange()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var format = new SpreadsheetFormatSpec(ColumnWidth: 30);

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "B", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.EditsApplied.Should().Be(1);
        command.ResultValue.PropertiesApplied.Should().Be(1);
        command.ResultValue.AutoFitApplied.Should().BeFalse();

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheet("Data").Column("B").Width.Should().BeApproximately(30, 0.01);
    }

    [Test]
    public async Task FormatRange_AppliesRowHeight_ToRowRange()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var format = new SpreadsheetFormatSpec(RowHeight: 36);

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "3", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.EditsApplied.Should().Be(1);
        command.ResultValue.PropertiesApplied.Should().Be(1);

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheet("Data").Row(3).Height.Should().BeApproximately(36, 0.1);
    }

    [Test]
    public async Task FormatRange_AutoFitColumns_AfterExplicitWidth_ColumnRange()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Cell("B1").Value = "Some content here";
        });

        // Set explicit width of 1 (very narrow), then autofit — should expand
        var format = new SpreadsheetFormatSpec(ColumnWidth: 1, AutoFitColumns: true);

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "B", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.AutoFitApplied.Should().BeTrue();
        command.ResultValue.PropertiesApplied.Should().Be(1);

        using var workbook = new XLWorkbook(_workbookPath);
        // Autofit should have overridden the explicit width of 1
        workbook.Worksheet("Data").Column("B").Width.Should().BeGreaterThan(1);
    }

    [Test]
    public async Task FormatRange_UnknownColor_ReturnsFailure()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var format = new SpreadsheetFormatSpec(BackgroundColor: "not-a-color");

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "A1", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Invalid color");
    }

    [Test]
    public async Task FormatRange_UnknownBorderStyle_ReturnsFailure()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var format = new SpreadsheetFormatSpec(
            Borders: new SpreadsheetBordersSpec(
                Top: new SpreadsheetBorderSide(Style: "SQUIGGLY")));

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "A1", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("border style");
    }

    [Test]
    public async Task FormatRange_AdjacentUnformattedCells_AreUntouched()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Cell("C1").Value = "untouched";
        });

        var format = new SpreadsheetFormatSpec(BackgroundColor: "#FF0000");

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "A1:B1", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Data");
        var appliedColor = ClosedXML.Excel.XLColor.FromHtml("#FF0000");

        // A1 and B1 received the background color
        sheet.Cell("A1").Style.Fill.BackgroundColor.Should().Be(appliedColor);
        sheet.Cell("B1").Style.Fill.BackgroundColor.Should().Be(appliedColor);
        // C1 was not in the range and must not have the applied background color
        sheet.Cell("C1").Style.Fill.BackgroundColor.Should().NotBe(appliedColor);
    }

    [Test]
    public async Task FormatRange_MissingSheet_ReturnsFailure()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var format = new SpreadsheetFormatSpec(TextFormat: new SpreadsheetTextFormat(Bold: true));

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Missing", "A1", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Missing");
    }

    [Test]
    public async Task FormatRange_MergeRange_MergesCellsAndPreservesTopLeftValue()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Cell("A1").Value = "Header";
        });

        var format = new SpreadsheetFormatSpec(
            BackgroundColor: "#FFFF00",
            MergeRange: true);

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "A1:C1", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.PropertiesApplied.Should().Be(2);

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Data");
        sheet.Range("A1:C1").IsMerged().Should().BeTrue();
        sheet.Cell("A1").GetString().Should().Be("Header");
        sheet.Cell("A1").Style.Fill.BackgroundColor.Should().Be(ClosedXML.Excel.XLColor.FromHtml("#FFFF00"));
    }

    [Test]
    public async Task FormatRange_MergeRange_OnColumnRange_ReturnsFailure()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var format = new SpreadsheetFormatSpec(MergeRange: true);

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "A:C", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("mergeRange");
    }

    [Test]
    public async Task FormatRange_BackgroundColorEmptyString_ClearsFill()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Cell("A1").Style.Fill.PatternType = XLFillPatternValues.Solid;
            sheet.Cell("A1").Style.Fill.BackgroundColor = XLColor.FromHtml("#FF0000");
        });

        var format = new SpreadsheetFormatSpec(BackgroundColor: string.Empty);

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "A1", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.PropertiesApplied.Should().Be(1);

        using var workbook = new XLWorkbook(_workbookPath);
        var style = workbook.Worksheet("Data").Cell("A1").Style;
        style.Fill.PatternType.Should().Be(XLFillPatternValues.None);
    }

    [Test]
    public async Task FormatRange_ForegroundColorEmptyString_ResetsFontColorToDefault()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Cell("A1").Style.Font.FontColor = XLColor.FromHtml("#FF0000");
        });

        var format = new SpreadsheetFormatSpec(
            TextFormat: new SpreadsheetTextFormat(ForegroundColor: string.Empty));

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "A1", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        var style = workbook.Worksheet("Data").Cell("A1").Style;
        style.Font.FontColor.Should().Be(workbook.Style.Font.FontColor);
    }

    [Test]
    public async Task FormatRange_BorderColorEmptyString_ResetsBorderColorToDefault()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Cell("A1").Style.Border.TopBorder = XLBorderStyleValues.Thin;
            sheet.Cell("A1").Style.Border.TopBorderColor = XLColor.FromHtml("#FF0000");
        });

        var format = new SpreadsheetFormatSpec(
            Borders: new SpreadsheetBordersSpec(
                Top: new SpreadsheetBorderSide(Color: string.Empty)));

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "A1", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        var style = workbook.Worksheet("Data").Cell("A1").Style;
        style.Border.TopBorderColor.Should().Be(workbook.Style.Border.TopBorderColor);
    }

    [Test]
    public async Task FormatRange_FontFamilyEmptyString_ResetsToWorkbookDefaultFont()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Cell("A1").Style.Font.FontName = "Arial";
        });

        var format = new SpreadsheetFormatSpec(
            TextFormat: new SpreadsheetTextFormat(FontFamily: string.Empty));

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "A1", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        var style = workbook.Worksheet("Data").Cell("A1").Style;
        style.Font.FontName.Should().Be(workbook.Style.Font.FontName);
    }

    [Test]
    public async Task FormatRange_FontSizeZero_ResetsToWorkbookDefaultSize()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Cell("A1").Style.Font.FontSize = 24;
        });

        var format = new SpreadsheetFormatSpec(
            TextFormat: new SpreadsheetTextFormat(FontSize: 0));

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "A1", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        var style = workbook.Worksheet("Data").Cell("A1").Style;
        style.Font.FontSize.Should().Be(workbook.Style.Font.FontSize);
    }

    [Test]
    public async Task FormatRange_ColumnWidthNegative_ResetsToWorkbookDefaultWidth()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Column("A").Width = 50;
        });

        var format = new SpreadsheetFormatSpec(ColumnWidth: -1);

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "A:A", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Data");
        sheet.Column("A").Width.Should().Be(workbook.ColumnWidth);
    }

    [Test]
    public async Task FormatRange_RowHeightNegative_ResetsToWorkbookDefaultHeight()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Row(1).Height = 80;
        });

        var format = new SpreadsheetFormatSpec(RowHeight: -1);

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "1:1", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Data");
        sheet.Row(1).Height.Should().Be(workbook.RowHeight);
    }

    [Test]
    public async Task FormatRange_MergeRangeFalse_UnmergesPreviouslyMergedRange()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Cell("A1").Value = "Header";
            sheet.Range("A1:C1").Merge();
        });

        var format = new SpreadsheetFormatSpec(MergeRange: false);

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "A1:C1", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.PropertiesApplied.Should().Be(1);

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Data");
        sheet.Range("A1:C1").IsMerged().Should().BeFalse();
    }

    [Test]
    public async Task FormatRange_MergeRangeFalse_OnUnmergedRange_DoesNotFail()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var format = new SpreadsheetFormatSpec(MergeRange: false);

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Data", "A1:C1", format)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.PropertiesApplied.Should().Be(1);
    }

    [Test]
    public async Task FreezePanes_FreezesRowsAndColumns()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var command = new FreezePanesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Data",
            Rows = 1,
            Columns = 2
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.Sheet.Should().Be("Data");
        command.ResultValue.Rows.Should().Be(1);
        command.ResultValue.Columns.Should().Be(2);

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Data");
        sheet.SheetView.SplitRow.Should().Be(1);
        sheet.SheetView.SplitColumn.Should().Be(2);
    }

    [Test]
    public async Task FreezePanes_MissingSheet_ReturnsFailure()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var command = new FreezePanesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Missing",
            Rows = 1
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Missing");
    }

    [Test]
    public async Task FreezePanes_NegativeArguments_ReturnFailure()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var command = new FreezePanesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Data",
            Rows = -1
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("non-negative");
    }

    [Test]
    public async Task SetActiveView_MakesSheetActive()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("First");
            workbook.Worksheets.Add("Summary");
        });

        var command = new SetActiveViewCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Summary"
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheet("Summary").TabActive.Should().BeTrue();
        workbook.Worksheet("First").TabActive.Should().BeFalse();
    }

    [Test]
    public async Task SetActiveView_ClearsTabSelectionOnOtherSheets()
    {
        CreateWorkbook(workbook =>
        {
            var first = workbook.Worksheets.Add("First");
            workbook.Worksheets.Add("Summary");
            first.TabSelected = true;
        });

        var command = new SetActiveViewCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Summary"
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheet("Summary").TabSelected.Should().BeTrue();
        workbook.Worksheet("First").TabSelected.Should().BeFalse();
    }

    [Test]
    public async Task SetActiveView_AppliesRangeSelectionAndActiveCell()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var command = new SetActiveViewCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Data",
            Range = "B2:D4"
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Data");
        sheet.SelectedRanges.Should().Contain(r => r.RangeAddress.ToStringRelative() == "B2:D4");
        sheet.ActiveCell.Should().NotBeNull();
        sheet.ActiveCell!.Address.ToStringRelative().Should().Be("B2");
    }

    [Test]
    public async Task SetActiveView_TopLeftCellA1_RoundTripsThroughGet()
    {
        // Mirrors the integration test:
        //   add_sheets(["Summary"])
        //   set_active_view("Summary", range="B2:D4", activeCell="C3", topLeftCell="A1")
        //   get_active_view -> {sheet:"Summary", range:"B2:D4", activeCell:"C3", topLeftCell:"A1"}
        // ClosedXML omits topLeftCell from OOXML when it equals the A1 default and
        // returns a zeroed address on reload; the reader treats that as "A1".
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Sheet1");
            workbook.Worksheets.Add("Summary");
        });

        var setCommand = new SetActiveViewCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Summary",
            Range = "B2:D4",
            ActiveCell = "C3",
            TopLeftCell = "A1"
        };
        var setResult = await setCommand.ExecuteAsync();
        setResult.IsSuccess.Should().BeTrue();

        var reader = new Celbridge.Spreadsheet.Services.SpreadsheetReader();
        var viewResult = reader.GetActiveView(_workbookPath);
        viewResult.IsSuccess.Should().BeTrue();
        var view = viewResult.Value;
        view.Sheet.Should().Be("Summary");
        view.Range.Should().Be("B2:D4");
        view.ActiveCell.Should().Be("C3");
        view.TopLeftCell.Should().Be("A1");
    }

    [Test]
    public async Task SetActiveView_AppliesTopLeftCell()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var command = new SetActiveViewCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Data",
            TopLeftCell = "A30"
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Data");
        sheet.SheetView.TopLeftCellAddress.ToStringRelative().Should().Be("A30");
    }

    [Test]
    public async Task SetActiveView_MissingSheet_ReturnsFailure()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var command = new SetActiveViewCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Nope"
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("not found");
    }

    [Test]
    public async Task SetActiveView_TopLeftCellAsRange_ReturnsFailure()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var command = new SetActiveViewCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Data",
            TopLeftCell = "A1:B2"
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("single cell");
    }

    [Test]
    public async Task SetActiveView_RangeWithSheetQualifier_ReturnsFailure()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var command = new SetActiveViewCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Data",
            Range = "Data!A1:B2"
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("sheet qualifier");
    }

    [Test]
    public async Task SetActiveView_ActiveCellInsideRange_AppliesAnchorCell()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var command = new SetActiveViewCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Data",
            Range = "B2:D4",
            ActiveCell = "C3"
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Data");
        sheet.ActiveCell.Should().NotBeNull();
        sheet.ActiveCell!.Address.ToStringRelative().Should().Be("C3");
        sheet.SelectedRanges.Should().Contain(r => r.RangeAddress.ToStringRelative() == "B2:D4");
    }

    [Test]
    public async Task SetActiveView_ActiveCellOnly_BecomesSingleCellSelection()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var command = new SetActiveViewCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Data",
            ActiveCell = "F8"
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Data");
        sheet.ActiveCell.Should().NotBeNull();
        sheet.ActiveCell!.Address.ToStringRelative().Should().Be("F8");
        sheet.SelectedRanges.Should().Contain(r => r.RangeAddress.ToStringRelative() == "F8:F8");
    }

    [Test]
    public async Task SetActiveView_ActiveCellOutsideRange_ReturnsFailure()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var command = new SetActiveViewCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Data",
            Range = "B2:D4",
            ActiveCell = "Z99"
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("inside Range");
    }

    [Test]
    public async Task SetActiveView_ActiveCellAsRange_ReturnsFailure()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var command = new SetActiveViewCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Data",
            ActiveCell = "B2:C3"
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("single cell");
    }

    [Test]
    public async Task ImportCsv_RowColumnCountMismatch_ReturnsFailure()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var csvText = "a,b,c\r\n1,2\r\n";

        var command = new ImportCsvCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Imports = new[]
            {
                new SpreadsheetCsvImport("Data", csvText)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("row 2");
    }

    [Test]
    public async Task FormatRanges_AppliesMultipleEditsAcrossSheetsInOneCall()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Q1");
            workbook.Worksheets.Add("Q2");
        });

        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Q1", "A1:B1", new SpreadsheetFormatSpec(
                    TextFormat: new SpreadsheetTextFormat(Bold: true))),
                new SpreadsheetFormatEdit("Q2", "A1:B1", new SpreadsheetFormatSpec(
                    BackgroundColor: "#FFFF00")),
                new SpreadsheetFormatEdit("Q1", "C1", new SpreadsheetFormatSpec(
                    HorizontalAlignment: "CENTER"))
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.EditsApplied.Should().Be(3);
        command.ResultValue.PropertiesApplied.Should().Be(3);

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheet("Q1").Cell("A1").Style.Font.Bold.Should().BeTrue();
        workbook.Worksheet("Q2").Cell("A1").Style.Fill.BackgroundColor
            .Should().Be(ClosedXML.Excel.XLColor.FromHtml("#FFFF00"));
        workbook.Worksheet("Q1").Cell("C1").Style.Alignment.Horizontal
            .Should().Be(ClosedXML.Excel.XLAlignmentHorizontalValues.Center);
    }

    [Test]
    public async Task FormatRanges_FailsAtomically_NoSaveOnPartialFailure()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Q1");
        });

        // First edit succeeds, second edit fails (bad colour) — batch should fail
        // and the workbook on disk must be unchanged.
        var command = new FormatRangesCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Edits = new[]
            {
                new SpreadsheetFormatEdit("Q1", "A1", new SpreadsheetFormatSpec(
                    TextFormat: new SpreadsheetTextFormat(Bold: true))),
                new SpreadsheetFormatEdit("Q1", "B1", new SpreadsheetFormatSpec(
                    BackgroundColor: "not-a-color"))
            }
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Edit 2");

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheet("Q1").Cell("A1").Style.Font.Bold.Should().BeFalse();
    }

    [Test]
    public async Task AddSheets_AddsMultipleSheetsInOneCall()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Existing");
        });

        var command = new AddSheetsCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheets = new[] { "Q1", "Q2", "Q3" }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.Sheets.Should().Equal("Q1", "Q2", "Q3");

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheets.Contains("Q1").Should().BeTrue();
        workbook.Worksheets.Contains("Q2").Should().BeTrue();
        workbook.Worksheets.Contains("Q3").Should().BeTrue();
        workbook.Worksheets.Count().Should().Be(4);
    }

    [Test]
    public async Task AddSheets_DuplicateNameInBatch_ReturnsFailureAndAddsNothing()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Existing");
        });

        var command = new AddSheetsCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheets = new[] { "Q1", "Q1" }
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Duplicate");

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheets.Contains("Q1").Should().BeFalse();
    }

    [Test]
    public async Task ImportCsv_ImportsMultipleSheetsInOneCall()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Existing");
        });

        var command = new ImportCsvCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Imports = new[]
            {
                new SpreadsheetCsvImport("Q1", "month,total\r\nJan,100\r\n", CreateIfMissing: true),
                new SpreadsheetCsvImport("Q2", "name\r\nAlpha\r\nBeta\r\n", CreateIfMissing: true)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.ImportsApplied.Should().Be(2);
        command.ResultValue.TotalRowCount.Should().Be(5);
        command.ResultValue.SheetsCreated.Should().Be(2);

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheet("Q1").Cell("A1").GetString().Should().Be("month");
        workbook.Worksheet("Q2").Cell("A2").GetString().Should().Be("Alpha");
    }

    [Test]
    public async Task Delete_RemovesRowsAndShiftsBelowUp()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "row1";
            sheet.Cell("A2").Value = "row2";
            sheet.Cell("A3").Value = "row3";
            sheet.Cell("A4").Value = "row4";
            sheet.Cell("A5").Value = "row5";
        });

        var command = new DeleteCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Operations = new[]
            {
                new SpreadsheetDeleteOperation("Q1", "2:3")
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.DeletedRowCount.Should().Be(2);

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Q1");
        sheet.Cell("A1").GetString().Should().Be("row1");
        sheet.Cell("A2").GetString().Should().Be("row4");
        sheet.Cell("A3").GetString().Should().Be("row5");
    }

    [Test]
    public async Task Delete_RemovesColumnsAndShiftsRightLeft()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "colA";
            sheet.Cell("B1").Value = "colB";
            sheet.Cell("C1").Value = "colC";
            sheet.Cell("D1").Value = "colD";
        });

        var command = new DeleteCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Operations = new[]
            {
                new SpreadsheetDeleteOperation("Q1", "B:C")
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.DeletedColumnCount.Should().Be(2);

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Q1");
        sheet.Cell("A1").GetString().Should().Be("colA");
        sheet.Cell("B1").GetString().Should().Be("colD");
    }

    [Test]
    public async Task Delete_OriginalCoordinateSemantics_AcrossMultipleOperations()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            for (int rowNumber = 1; rowNumber <= 12; rowNumber++)
            {
                sheet.Cell($"A{rowNumber}").Value = $"row{rowNumber}";
            }
        });

        // Two ops referring to the original coordinate space:
        // delete rows 3:5 AND row 10. After applying, the remaining rows should be
        // 1, 2, 6, 7, 8, 9, 11, 12 — original row 10 is gone, not "row 10 after the
        // earlier delete shifted it" (which would have been original row 13).
        var command = new DeleteCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Operations = new[]
            {
                new SpreadsheetDeleteOperation("Q1", "3:5"),
                new SpreadsheetDeleteOperation("Q1", "10")
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.DeletedRowCount.Should().Be(4);

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Q1");
        sheet.Cell("A1").GetString().Should().Be("row1");
        sheet.Cell("A2").GetString().Should().Be("row2");
        sheet.Cell("A3").GetString().Should().Be("row6");
        sheet.Cell("A4").GetString().Should().Be("row7");
        sheet.Cell("A5").GetString().Should().Be("row8");
        sheet.Cell("A6").GetString().Should().Be("row9");
        sheet.Cell("A7").GetString().Should().Be("row11");
        sheet.Cell("A8").GetString().Should().Be("row12");
    }

    [Test]
    public async Task Delete_OverlappingRanges_AreDeduped()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            for (int rowNumber = 1; rowNumber <= 10; rowNumber++)
            {
                sheet.Cell($"A{rowNumber}").Value = $"row{rowNumber}";
            }
        });

        // Overlapping row ranges 3:5 and 4:6 should expand to {3,4,5,6}.
        var command = new DeleteCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Operations = new[]
            {
                new SpreadsheetDeleteOperation("Q1", "3:5"),
                new SpreadsheetDeleteOperation("Q1", "4:6")
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.DeletedRowCount.Should().Be(4);

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Q1");
        sheet.Cell("A1").GetString().Should().Be("row1");
        sheet.Cell("A2").GetString().Should().Be("row2");
        sheet.Cell("A3").GetString().Should().Be("row7");
    }

    [Test]
    public async Task Delete_RejectsCellRange()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Q1");
        });

        var command = new DeleteCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Operations = new[]
            {
                new SpreadsheetDeleteOperation("Q1", "A1:C3")
            }
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("not a row or column range");
    }

    [Test]
    public async Task Delete_MissingSheet_ReturnsFailure_AtomicNoSave()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "before";
        });

        var command = new DeleteCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Operations = new[]
            {
                new SpreadsheetDeleteOperation("Q1", "1"),
                new SpreadsheetDeleteOperation("Missing", "1")
            }
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Missing");

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheet("Q1").Cell("A1").GetString().Should().Be("before");
    }

    [Test]
    public async Task Clear_ClearsCellRange_LeavesOtherCellsAlone()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "keep";
            sheet.Cell("B2").Value = "wipe";
            sheet.Cell("C2").Value = "wipe";
            sheet.Cell("D5").Value = "keep";
        });

        var command = new ClearCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Operations = new[]
            {
                new SpreadsheetClearOperation("Q1", "B2:C2")
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.CellCount.Should().Be(2);

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Q1");
        sheet.Cell("A1").GetString().Should().Be("keep");
        sheet.Cell("B2").Value.IsBlank.Should().BeTrue();
        sheet.Cell("C2").Value.IsBlank.Should().BeTrue();
        sheet.Cell("D5").GetString().Should().Be("keep");
    }

    [Test]
    public async Task Clear_EmptyRange_ClearsEntireSheet_PreservesIdentity()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "wipe";
            sheet.Cell("B2").Value = "wipe";
            sheet.SheetView.FreezeRows(1);
            workbook.DefinedNames.Add("MyRange", "Q1!A1:A1");
        });

        var command = new ClearCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Operations = new[]
            {
                new SpreadsheetClearOperation("Q1", "")
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheets.Contains("Q1").Should().BeTrue();
        var sheet = workbook.Worksheet("Q1");
        sheet.Cell("A1").Value.IsBlank.Should().BeTrue();
        sheet.Cell("B2").Value.IsBlank.Should().BeTrue();
        sheet.SheetView.SplitRow.Should().Be(1);
        workbook.DefinedNames.Any(n => n.Name == "MyRange").Should().BeTrue();
    }

    [Test]
    public async Task Clear_DoesNotShiftCells()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "row1";
            sheet.Cell("A2").Value = "row2";
            sheet.Cell("A3").Value = "row3";
        });

        var command = new ClearCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Operations = new[]
            {
                new SpreadsheetClearOperation("Q1", "2")
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Q1");
        sheet.Cell("A1").GetString().Should().Be("row1");
        sheet.Cell("A2").Value.IsBlank.Should().BeTrue();
        sheet.Cell("A3").GetString().Should().Be("row3");
    }

    [Test]
    public async Task Clear_CountsFormattingOnlyCells()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            // B2 has a value, C3 has only a background colour, D4 is fully default.
            sheet.Cell("B2").Value = "value";
            sheet.Cell("C3").Style.Fill.BackgroundColor = XLColor.Yellow;
        });

        var command = new ClearCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Operations = new[]
            {
                new SpreadsheetClearOperation("Q1", "B2:D4")
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        // B2 (value) and C3 (formatting-only) both count; D4 was already default.
        command.ResultValue.CellCount.Should().Be(2);

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Q1");
        sheet.Cell("B2").Value.IsBlank.Should().BeTrue();
    }

    [Test]
    public async Task Clear_EmptyRange_CountsFormattingOnlyCells()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Style.Fill.BackgroundColor = XLColor.Yellow;
            sheet.Cell("B2").Style.Font.Bold = true;
        });

        var command = new ClearCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Operations = new[]
            {
                new SpreadsheetClearOperation("Q1", string.Empty)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.CellCount.Should().Be(2);
    }

    [Test]
    public async Task Clear_MissingSheet_ReturnsFailure_AtomicNoSave()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "before";
        });

        var command = new ClearCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Operations = new[]
            {
                new SpreadsheetClearOperation("Q1", "A1"),
                new SpreadsheetClearOperation("Missing", "A1")
            }
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Missing");

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheet("Q1").Cell("A1").GetString().Should().Be("before");
    }

    [Test]
    public async Task Insert_AddsRowsAndShiftsExistingDown()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "row1";
            sheet.Cell("A2").Value = "row2";
            sheet.Cell("A3").Value = "row3";
        });

        var command = new InsertCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Operations = new[]
            {
                new SpreadsheetInsertOperation("Q1", "2:3")
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.InsertedRowCount.Should().Be(2);
        command.ResultValue.InsertedColumnCount.Should().Be(0);

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Q1");
        sheet.Cell("A1").GetString().Should().Be("row1");
        sheet.Cell("A2").Value.IsBlank.Should().BeTrue();
        sheet.Cell("A3").Value.IsBlank.Should().BeTrue();
        sheet.Cell("A4").GetString().Should().Be("row2");
        sheet.Cell("A5").GetString().Should().Be("row3");
    }

    [Test]
    public async Task Insert_AddsColumnsAndShiftsExistingRight()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "col1";
            sheet.Cell("B1").Value = "col2";
            sheet.Cell("C1").Value = "col3";
        });

        var command = new InsertCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Operations = new[]
            {
                new SpreadsheetInsertOperation("Q1", "B")
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.InsertedColumnCount.Should().Be(1);

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Q1");
        sheet.Cell("A1").GetString().Should().Be("col1");
        sheet.Cell("B1").Value.IsBlank.Should().BeTrue();
        sheet.Cell("C1").GetString().Should().Be("col2");
        sheet.Cell("D1").GetString().Should().Be("col3");
    }

    [Test]
    public async Task Insert_OriginalCoordinateSemantics_AcrossMultipleOperations()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            for (int rowIndex = 1; rowIndex <= 5; rowIndex++)
            {
                sheet.Cell(rowIndex, 1).Value = $"row{rowIndex}";
            }
        });

        var command = new InsertCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Operations = new[]
            {
                new SpreadsheetInsertOperation("Q1", "2"),
                new SpreadsheetInsertOperation("Q1", "5")
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.InsertedRowCount.Should().Be(2);

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Q1");
        // Original rows 1, 2, 3, 4, 5 land at 1, 3, 4, 6, 7 respectively
        // because we inserted before original rows 2 and 5.
        sheet.Cell("A1").GetString().Should().Be("row1");
        sheet.Cell("A2").Value.IsBlank.Should().BeTrue();
        sheet.Cell("A3").GetString().Should().Be("row2");
        sheet.Cell("A4").GetString().Should().Be("row3");
        sheet.Cell("A5").GetString().Should().Be("row4");
        sheet.Cell("A6").Value.IsBlank.Should().BeTrue();
        sheet.Cell("A7").GetString().Should().Be("row5");
    }

    [Test]
    public async Task Insert_RejectsCellRange()
    {
        CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Q1");
        });

        var command = new InsertCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Operations = new[]
            {
                new SpreadsheetInsertOperation("Q1", "A1:C3")
            }
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("not a row or column range");
    }

    [Test]
    public async Task Insert_MissingSheet_ReturnsFailure_AtomicNoSave()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "before";
        });

        var command = new InsertCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Operations = new[]
            {
                new SpreadsheetInsertOperation("Q1", "1"),
                new SpreadsheetInsertOperation("Missing", "1")
            }
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Missing");

        using var workbook = new XLWorkbook(_workbookPath);
        workbook.Worksheet("Q1").Cell("A1").GetString().Should().Be("before");
    }

    [Test]
    public async Task Sort_SortsByColumnAscending()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "Charlie";
            sheet.Cell("A2").Value = "Alpha";
            sheet.Cell("A3").Value = "Bravo";
        });

        var command = new SortCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Q1",
            Range = "A1:A3",
            SortKeys = new[]
            {
                new SpreadsheetSortKey("A", Ascending: true)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.RowCount.Should().Be(3);

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Q1");
        sheet.Cell("A1").GetString().Should().Be("Alpha");
        sheet.Cell("A2").GetString().Should().Be("Bravo");
        sheet.Cell("A3").GetString().Should().Be("Charlie");
    }

    [Test]
    public async Task Sort_HasHeaderRow_LeavesHeaderInPlace()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "Name";
            sheet.Cell("A2").Value = "Charlie";
            sheet.Cell("A3").Value = "Alpha";
            sheet.Cell("A4").Value = "Bravo";
        });

        var command = new SortCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Q1",
            Range = "A1:A4",
            SortKeys = new[]
            {
                new SpreadsheetSortKey("A", Ascending: true)
            },
            HasHeaderRow = true
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.RowCount.Should().Be(3);

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Q1");
        sheet.Cell("A1").GetString().Should().Be("Name");
        sheet.Cell("A2").GetString().Should().Be("Alpha");
        sheet.Cell("A3").GetString().Should().Be("Bravo");
        sheet.Cell("A4").GetString().Should().Be("Charlie");
    }

    [Test]
    public async Task Sort_MultiColumn_PrimaryThenSecondary()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "B";
            sheet.Cell("B1").Value = 2;
            sheet.Cell("A2").Value = "A";
            sheet.Cell("B2").Value = 2;
            sheet.Cell("A3").Value = "A";
            sheet.Cell("B3").Value = 1;
            sheet.Cell("A4").Value = "B";
            sheet.Cell("B4").Value = 1;
        });

        var command = new SortCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Q1",
            Range = "A1:B4",
            SortKeys = new[]
            {
                new SpreadsheetSortKey("A", Ascending: true),
                new SpreadsheetSortKey("B", Ascending: false)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        using var workbook = new XLWorkbook(_workbookPath);
        var sheet = workbook.Worksheet("Q1");
        sheet.Cell("A1").GetString().Should().Be("A");
        sheet.Cell("B1").GetDouble().Should().Be(2);
        sheet.Cell("A2").GetString().Should().Be("A");
        sheet.Cell("B2").GetDouble().Should().Be(1);
        sheet.Cell("A3").GetString().Should().Be("B");
        sheet.Cell("B3").GetDouble().Should().Be(2);
        sheet.Cell("A4").GetString().Should().Be("B");
        sheet.Cell("B4").GetDouble().Should().Be(1);
    }

    [Test]
    public async Task Sort_ColumnOutsideRange_ReturnsFailure()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "Charlie";
            sheet.Cell("A2").Value = "Alpha";
        });

        var command = new SortCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Q1",
            Range = "A1:A2",
            SortKeys = new[]
            {
                new SpreadsheetSortKey("B", Ascending: true)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("outside the sort range");
    }

    [Test]
    public async Task Sort_EmptyRange_DefaultsToUsedRange()
    {
        CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "Charlie";
            sheet.Cell("A2").Value = "Alpha";
            sheet.Cell("A3").Value = "Bravo";
        });

        var command = new SortCommand(_workspaceWrapper)
        {
            FileResource = _workbookResource,
            Sheet = "Q1",
            Range = string.Empty,
            SortKeys = new[]
            {
                new SpreadsheetSortKey("A", Ascending: true)
            }
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.RowCount.Should().Be(3);
    }

    private void CreateWorkbook(Action<XLWorkbook> populate)
    {
        using var workbook = new XLWorkbook();
        populate(workbook);
        workbook.SaveAs(_workbookPath);
    }
}
