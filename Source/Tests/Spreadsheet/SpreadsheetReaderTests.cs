using Celbridge.Spreadsheet;
using Celbridge.Spreadsheet.Services;
using ClosedXML.Excel;

namespace Celbridge.Tests.Spreadsheet;

/// <summary>
/// Tests for SpreadsheetReader against fixture .xlsx workbooks generated in
/// SetUp via ClosedXML. Covers the happy path and the most common failure
/// modes for each read entry point.
/// </summary>
[TestFixture]
public class SpreadsheetReaderTests
{
    private string _tempFolder = null!;
    private SpreadsheetReader _reader = null!;

    [SetUp]
    public void SetUp()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(SpreadsheetReaderTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);
        _reader = new SpreadsheetReader();
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
    public void GetInfo_ReturnsSheetsAndUsedRange()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "month";
            sheet.Cell("B1").Value = "total";
            sheet.Cell("A2").Value = "Jan";
            sheet.Cell("B2").Value = 100;
            sheet.Cell("A3").Value = "Feb";
            sheet.Cell("B3").Value = 200;

            workbook.Worksheets.Add("Empty");
        });

        var result = _reader.GetInfo(OpenWorkbook(workbookPath));

        result.IsSuccess.Should().BeTrue();
        var info = result.Value;
        info.Sheets.Should().HaveCount(2);

        var q1 = info.Sheets[0];
        q1.Name.Should().Be("Q1");
        q1.Position.Should().Be(1);
        q1.UsedRange.Should().Be("A1:B3");
        q1.RowCount.Should().Be(3);
        q1.ColumnCount.Should().Be(2);

        var empty = info.Sheets[1];
        empty.Name.Should().Be("Empty");
        empty.Position.Should().Be(2);
        empty.UsedRange.Should().BeNull();
        empty.RowCount.Should().Be(0);
        empty.ColumnCount.Should().Be(0);
    }

    [Test]
    public void ReadSheet_ReturnsRowArrays()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "month";
            sheet.Cell("B1").Value = "total";
            sheet.Cell("A2").Value = "Jan";
            sheet.Cell("B2").Value = 100;
        });

        var result = _reader.ReadSheet(OpenWorkbook(workbookPath), "Q1", new ReadOptions());

        result.IsSuccess.Should().BeTrue();
        var read = result.Value;
        read.TotalRowCount.Should().Be(2);
        read.Rows.Should().HaveCount(2);

        var headerRow = (object?[])read.Rows[0]!;
        headerRow[0].Should().Be("month");
        headerRow[1].Should().Be("total");

        var firstDataRow = (object?[])read.Rows[1]!;
        firstDataRow[0].Should().Be("Jan");
        firstDataRow[1].Should().Be(100.0);
    }

    [Test]
    public void ReadSheet_HeadersMode_ReturnsRowDictionaries()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "month";
            sheet.Cell("B1").Value = "total";
            sheet.Cell("A2").Value = "Jan";
            sheet.Cell("B2").Value = 100;
            sheet.Cell("A3").Value = "Feb";
            sheet.Cell("B3").Value = 200;
        });

        var options = new ReadOptions(Headers: true);
        var result = _reader.ReadSheet(OpenWorkbook(workbookPath), "Q1", options);

        result.IsSuccess.Should().BeTrue();
        var read = result.Value;
        read.Headers.Should().Equal("month", "total");
        read.TotalRowCount.Should().Be(2);
        read.Rows.Should().HaveCount(2);

        var firstRow = (Dictionary<string, object?>)read.Rows[0]!;
        firstRow["month"].Should().Be("Jan");
        firstRow["total"].Should().Be(100.0);
    }

    [Test]
    public void ReadSheet_HeadersMode_DisambiguatesDuplicatesAndEmptyHeaders()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Sheet1");
            sheet.Cell("A1").Value = "name";
            // B1 left blank
            sheet.Cell("C1").Value = "name";
            sheet.Cell("A2").Value = "x";
            sheet.Cell("B2").Value = "y";
            sheet.Cell("C2").Value = "z";
        });

        var options = new ReadOptions(Headers: true);
        var result = _reader.ReadSheet(OpenWorkbook(workbookPath), "Sheet1", options);

        result.IsSuccess.Should().BeTrue();
        var read = result.Value;
        read.Headers.Should().Equal("name", "column_B", "name_2");

        var row = (Dictionary<string, object?>)read.Rows[0]!;
        row["name"].Should().Be("x");
        row["column_B"].Should().Be("y");
        row["name_2"].Should().Be("z");
    }

    [Test]
    public void ReadSheet_FormulasMode_ReturnsFormulaText()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = 10;
            sheet.Cell("A2").Value = 20;
            sheet.Cell("A3").FormulaA1 = "SUM(A1:A2)";
        });

        var options = new ReadOptions(Mode: SpreadsheetReadMode.Formulas);
        var result = _reader.ReadSheet(OpenWorkbook(workbookPath), "Q1", options);

        result.IsSuccess.Should().BeTrue();
        var read = result.Value;
        var thirdRow = (object?[])read.Rows[2]!;
        thirdRow[0].Should().Be("=SUM(A1:A2)");
    }

    [Test]
    public void ReadSheet_EmptySheet_ReturnsEmptyRowsAndZeroTotal()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Empty");
        });

        var result = _reader.ReadSheet(OpenWorkbook(workbookPath), "Empty", new ReadOptions());

        result.IsSuccess.Should().BeTrue();
        result.Value.Rows.Should().BeEmpty();
        result.Value.TotalRowCount.Should().Be(0);
    }

    [Test]
    public void ReadSheet_MissingSheet_ReturnsFailure()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Sheet1");
        });

        var result = _reader.ReadSheet(OpenWorkbook(workbookPath), "Missing", new ReadOptions());

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Missing");
    }

    [Test]
    public void ReadSheet_RangeWithSheetQualifier_ReturnsFailure()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = 1;
        });

        var options = new ReadOptions(Range: "Q1!A1:B2");
        var result = _reader.ReadSheet(OpenWorkbook(workbookPath), "Q1", options);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("sheet qualifier");
    }

    [Test]
    public void ReadSheet_OffsetAndLimitPageThroughRows()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            for (int i = 1; i <= 10; i++)
            {
                sheet.Cell($"A{i}").Value = i;
            }
        });

        var options = new ReadOptions(Offset: 3, Limit: 4);
        var result = _reader.ReadSheet(OpenWorkbook(workbookPath), "Q1", options);

        result.IsSuccess.Should().BeTrue();
        var read = result.Value;
        read.TotalRowCount.Should().Be(10);
        read.Rows.Should().HaveCount(4);
        var firstRow = (object?[])read.Rows[0]!;
        firstRow[0].Should().Be(4.0);
    }

    [Test]
    public void ExportCsv_RoundTripsValuesAndQuotesSpecialFields()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "name";
            sheet.Cell("B1").Value = "note";
            sheet.Cell("A2").Value = "Smith, John";
            sheet.Cell("B2").Value = "He said \"hi\"";
            sheet.Cell("A3").Value = "Multi";
            sheet.Cell("B3").Value = "line1\nline2";
        });

        var result = _reader.ExportCsv(OpenWorkbook(workbookPath), "Q1", null);

        result.IsSuccess.Should().BeTrue();
        var csvResult = result.Value;
        csvResult.RowCount.Should().Be(3);
        csvResult.ColumnCount.Should().Be(2);
        var csv = csvResult.Csv;
        csv.Should().Contain("\"Smith, John\"");
        csv.Should().Contain("\"He said \"\"hi\"\"\"");
        csv.Should().Contain("\"line1\nline2\"");
        csv.Should().EndWith("\r\n");
    }

    [Test]
    public void ExportCsv_EmptySheet_ReturnsEmptyResult()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Empty");
        });

        var result = _reader.ExportCsv(OpenWorkbook(workbookPath), "Empty", null);

        result.IsSuccess.Should().BeTrue();
        var csvResult = result.Value;
        csvResult.Csv.Should().BeEmpty();
        csvResult.RowCount.Should().Be(0);
        csvResult.ColumnCount.Should().Be(0);
    }

    [Test]
    public void ReadFormat_ReturnsFormatForFormattedCell()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Cell("A1").Style.Font.Bold = true;
            sheet.Cell("A1").Style.Fill.PatternType = XLFillPatternValues.Solid;
            sheet.Cell("A1").Style.Fill.BackgroundColor = XLColor.FromHtml("#D3D3D3");
            sheet.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        });

        var result = _reader.ReadFormat(OpenWorkbook(workbookPath), "Data", "A1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Range.Should().Be("Data!A1:A1");
        result.Value.Rows.Should().HaveCount(1);

        var spec = result.Value.Rows[0][0];
        spec.TextFormat!.Bold.Should().BeTrue();
        spec.BackgroundColor.Should().Be("#D3D3D3");
        spec.HorizontalAlignment.Should().Be("CENTER");
    }

    [Test]
    public void ReadFormat_UnformattedCell_EmitsClearSentinelsForRoundTrip()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Cell("A1").Value = "plain";
        });

        var result = _reader.ReadFormat(OpenWorkbook(workbookPath), "Data", "A1");

        result.IsSuccess.Should().BeTrue();
        var spec = result.Value.Rows[0][0];
        spec.TextFormat!.Bold.Should().BeNull();
        // No fill emits the clear-fill sentinel "" so the round-trip writes
        // "no fill" onto the destination rather than leaving its previous fill.
        spec.BackgroundColor.Should().Be(string.Empty);
        spec.Borders.Should().BeNull();
        spec.HorizontalAlignment.Should().BeNull();
        spec.WrapText.Should().BeNull();
    }

    [Test]
    public void ReadFormat_MultiCellRange_ReturnsMappedGrid()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Cell("A1").Style.Font.Bold = true;
            sheet.Cell("B1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.Cell("A2").Style.Fill.PatternType = XLFillPatternValues.Solid;
            sheet.Cell("A2").Style.Fill.BackgroundColor = XLColor.FromHtml("#FFFF00");
        });

        var result = _reader.ReadFormat(OpenWorkbook(workbookPath), "Data", "A1:B2");

        result.IsSuccess.Should().BeTrue();
        result.Value.Rows.Should().HaveCount(2);
        result.Value.Rows[0].Should().HaveCount(2);
        result.Value.Rows[0][0].TextFormat!.Bold.Should().BeTrue();
        result.Value.Rows[0][1].HorizontalAlignment.Should().Be("CENTER");
        result.Value.Rows[1][0].BackgroundColor.Should().Be("#FFFF00");
        result.Value.Rows[1][1].BackgroundColor.Should().Be(string.Empty);
    }

    [Test]
    public void ReadFormat_Borders_RoundTripsStyleAndColor()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Cell("A1").Style.Border.TopBorder = XLBorderStyleValues.Thin;
            sheet.Cell("A1").Style.Border.TopBorderColor = XLColor.FromHtml("#FF0000");
            sheet.Cell("A1").Style.Border.BottomBorder = XLBorderStyleValues.Dashed;
        });

        var result = _reader.ReadFormat(OpenWorkbook(workbookPath), "Data", "A1");

        result.IsSuccess.Should().BeTrue();
        var spec = result.Value.Rows[0][0];
        spec.Borders!.Top!.Style.Should().Be("SOLID");
        spec.Borders.Top.Color.Should().Be("#FF0000");
        spec.Borders.Bottom!.Style.Should().Be("DASHED");
        spec.Borders.Left.Should().BeNull();
        spec.Borders.Right.Should().BeNull();
    }

    [Test]
    public void ReadFormat_EmptyRange_ReadsUsedRange()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Cell("A1").Value = "header";
            sheet.Cell("A1").Style.Font.Bold = true;
            sheet.Cell("B2").Value = 42;
            sheet.Cell("B2").Style.Fill.PatternType = XLFillPatternValues.Solid;
            sheet.Cell("B2").Style.Fill.BackgroundColor = XLColor.FromHtml("#FFFF00");
        });

        var result = _reader.ReadFormat(OpenWorkbook(workbookPath), "Data", null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Rows.Should().HaveCount(2);
        result.Value.Rows[0].Should().HaveCount(2);
    }

    [Test]
    public void ReadFormat_MissingSheet_ReturnsFailure()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Data");
        });

        var result = _reader.ReadFormat(OpenWorkbook(workbookPath), "Missing", null);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Missing");
    }

    [Test]
    public void GetInfo_ReturnsFrozenPaneCounts()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.SheetView.FreezeRows(2);
            sheet.SheetView.FreezeColumns(3);
            workbook.Worksheets.Add("Plain");
        });

        var result = _reader.GetInfo(OpenWorkbook(workbookPath));

        result.IsSuccess.Should().BeTrue();
        var sheets = result.Value.Sheets;
        sheets[0].FrozenRows.Should().Be(2);
        sheets[0].FrozenColumns.Should().Be(3);
        sheets[1].FrozenRows.Should().Be(0);
        sheets[1].FrozenColumns.Should().Be(0);
    }

    [Test]
    public void GetActiveView_ReturnsActiveSheetSelectionAndScroll()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("First");
            var data = workbook.Worksheets.Add("Data");
            data.SetTabActive();
            data.TabSelected = true;
            data.ActiveCell = data.Cell("C3");
            data.SelectedRanges.RemoveAll(_ => true);
            data.SelectedRanges.Add(data.Range("B2:D5"));
            data.SheetView.TopLeftCellAddress = data.Cell("A10").Address;
        });

        var result = _reader.GetActiveView(OpenWorkbook(workbookPath));

        result.IsSuccess.Should().BeTrue();
        var view = result.Value;
        view.Sheet.Should().Be("Data");
        view.Range.Should().Be("B2:D5");
        view.ActiveCell.Should().Be("C3");
        view.TopLeftCell.Should().Be("A10");
    }

    [Test]
    public void GetActiveView_SingleCellSelection_CollapsesRange()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Sheet1");
            sheet.SetTabActive();
            sheet.TabSelected = true;
            sheet.ActiveCell = sheet.Cell("D5");
            sheet.SelectedRanges.RemoveAll(_ => true);
            sheet.SelectedRanges.Add(sheet.Range("D5"));
        });

        var result = _reader.GetActiveView(OpenWorkbook(workbookPath));

        result.IsSuccess.Should().BeTrue();
        result.Value.Range.Should().Be("D5");
        result.Value.Ranges.Should().Equal("D5");
        result.Value.ActiveCell.Should().Be("D5");
    }

    [Test]
    public void GetActiveView_MultiRangeSelection_ReturnsAllRanges()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Sheet1");
            sheet.SetTabActive();
            sheet.TabSelected = true;
            sheet.ActiveCell = sheet.Cell("A7");
            sheet.SelectedRanges.RemoveAll(_ => true);
            sheet.SelectedRanges.Add(sheet.Range("A7:B8"));
            sheet.SelectedRanges.Add(sheet.Range("A12:B13"));
        });

        var result = _reader.GetActiveView(OpenWorkbook(workbookPath));

        result.IsSuccess.Should().BeTrue();
        var view = result.Value;
        view.Range.Should().Be("A7:B8");
        view.Ranges.Should().Equal("A7:B8", "A12:B13");
        view.ActiveCell.Should().Be("A7");
    }

    [Test]
    public void GetActiveView_MultiCellSelection_FiltersDegenerateActiveCellRange()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Sheet1");
            sheet.SetTabActive();
            sheet.TabSelected = true;
            sheet.ActiveCell = sheet.Cell("C3");
            sheet.SelectedRanges.RemoveAll(_ => true);
            sheet.SelectedRanges.Add(sheet.Range("B2:D5"));
        });

        var result = _reader.GetActiveView(OpenWorkbook(workbookPath));

        result.IsSuccess.Should().BeTrue();
        var view = result.Value;
        view.Range.Should().Be("B2:D5");
        view.Ranges.Should().Equal("B2:D5");
        view.ActiveCell.Should().Be("C3");
    }

    [Test]
    public void Find_FindsTextSubstringsAcrossSheets()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheetA = workbook.Worksheets.Add("Q1");
            sheetA.Cell("A1").Value = "Hello World";
            sheetA.Cell("A2").Value = "no match";
            var sheetB = workbook.Worksheets.Add("Q2");
            sheetB.Cell("B5").Value = "Hello Friend";
        });

        var options = new FindOptions(Find: "Hello", Sheet: "", Range: "", MatchCase: false, MatchEntireCellContents: false);
        var result = _reader.Find(OpenWorkbook(workbookPath), options);

        result.IsSuccess.Should().BeTrue();
        result.Value.MatchCount.Should().Be(2);
        result.Value.Matches.Should().Contain(m => m.Sheet == "Q1" && m.Cell == "A1");
        result.Value.Matches.Should().Contain(m => m.Sheet == "Q2" && m.Cell == "B5");
    }

    [Test]
    public void Find_MatchesFormulaExpressionText()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = 1;
            sheet.Cell("A2").Value = 2;
            sheet.Cell("B1").FormulaA1 = "=SUM(A1:A2)";
        });

        var options = new FindOptions(Find: "SUM", Sheet: "Q1", Range: "", MatchCase: false, MatchEntireCellContents: false);
        var result = _reader.Find(OpenWorkbook(workbookPath), options);

        result.IsSuccess.Should().BeTrue();
        result.Value.MatchCount.Should().Be(1);
        var match = result.Value.Matches[0];
        match.Cell.Should().Be("B1");
        match.IsFormula.Should().BeTrue();
        match.Text.Should().Contain("SUM");
    }

    [Test]
    public void Find_MatchEntireCellContents_OnlyExactMatches()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "foo";
            sheet.Cell("A2").Value = "foobar";
            sheet.Cell("A3").Value = "foo bar";
        });

        var options = new FindOptions(Find: "foo", Sheet: "Q1", Range: "", MatchCase: false, MatchEntireCellContents: true);
        var result = _reader.Find(OpenWorkbook(workbookPath), options);

        result.IsSuccess.Should().BeTrue();
        result.Value.MatchCount.Should().Be(1);
        result.Value.Matches[0].Cell.Should().Be("A1");
    }

    [Test]
    public void Find_RangeLimitsScope()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "needle";
            sheet.Cell("B2").Value = "needle";
            sheet.Cell("D5").Value = "needle";
        });

        var options = new FindOptions(Find: "needle", Sheet: "Q1", Range: "A1:C3", MatchCase: false, MatchEntireCellContents: false);
        var result = _reader.Find(OpenWorkbook(workbookPath), options);

        result.IsSuccess.Should().BeTrue();
        result.Value.MatchCount.Should().Be(2);
        result.Value.Matches.Should().NotContain(m => m.Cell == "D5");
    }

    [Test]
    public void Find_RangeWithoutSheet_Fails()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            workbook.Worksheets.Add("Q1");
        });

        var options = new FindOptions(Find: "x", Sheet: "", Range: "A1:C3", MatchCase: false, MatchEntireCellContents: false);
        var result = _reader.Find(OpenWorkbook(workbookPath), options);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Range can only be used together with a specific sheet");
    }

    [Test]
    public void Find_MatchCase_DistinguishesCase()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "Hello";
            sheet.Cell("A2").Value = "hello";
            sheet.Cell("A3").Value = "HELLO";
        });

        var caseSensitive = new FindOptions(Find: "Hello", Sheet: "Q1", Range: "", MatchCase: true, MatchEntireCellContents: false);
        var caseSensitiveResult = _reader.Find(OpenWorkbook(workbookPath), caseSensitive);

        caseSensitiveResult.IsSuccess.Should().BeTrue();
        caseSensitiveResult.Value.MatchCount.Should().Be(1);
        caseSensitiveResult.Value.Matches[0].Cell.Should().Be("A1");

        var caseInsensitive = caseSensitive with { MatchCase = false };
        var caseInsensitiveResult = _reader.Find(OpenWorkbook(workbookPath), caseInsensitive);

        caseInsensitiveResult.IsSuccess.Should().BeTrue();
        caseInsensitiveResult.Value.MatchCount.Should().Be(3);
    }

    [Test]
    public void Find_NoMatches_ReturnsEmpty()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.Worksheets.Add("Q1");
            sheet.Cell("A1").Value = "alpha";
        });

        var options = new FindOptions(Find: "missing", Sheet: "", Range: "", MatchCase: false, MatchEntireCellContents: false);
        var result = _reader.Find(OpenWorkbook(workbookPath), options);

        result.IsSuccess.Should().BeTrue();
        result.Value.MatchCount.Should().Be(0);
        result.Value.Matches.Should().BeEmpty();
    }

    private string CreateWorkbook(Action<XLWorkbook> populate)
    {
        var workbookPath = Path.Combine(_tempFolder, $"{Guid.NewGuid():N}.xlsx");
        using var workbook = new XLWorkbook();
        populate(workbook);
        workbook.SaveAs(workbookPath);
        return workbookPath;
    }

    private static Stream OpenWorkbook(string path) => new MemoryStream(File.ReadAllBytes(path));
}
