# Spreadsheet workflows

Common end-to-end tasks expressed as tool sequences. Each example uses the Python proxy form; the MCP / JS forms are equivalent (see `tool_naming`).

## Inspect a workbook before reading

```python
spreadsheet.get_info(resource="data/sales.xlsx")
spreadsheet.read_sheet(resource="data/sales.xlsx", sheet="Q1", headers=True)
```

`get_info` returns sheet names, used ranges, row and column counts, frozen-pane counts, and any defined names. Always cheap; always the right first call.

## Read-modify-write a single cell

```python
spreadsheet.read_sheet(resource="data/sales.xlsx", sheet="Q1", range="B3")
spreadsheet.write_cells(
    resource="data/sales.xlsx",
    sheet="Q1",
    edits=[{"cell": "B3", "value": 42}])
```

## Write a formula and read its computed value

```python
spreadsheet.write_cells(
    resource="data/sales.xlsx",
    sheet="Q1",
    edits=[{"cell": "C3", "value": "=SUM(A1:A10)", "isFormula": True}])
spreadsheet.read_sheet(resource="data/sales.xlsx", sheet="Q1", range="C3")
```

Formula recalculation runs as part of the write, so the read sees the up-to-date computed value. See `spreadsheet_cell_typing` for the formula-vs-text rules.

## Append rows to the end of a sheet

```python
spreadsheet.append_rows(
    resource="data/sales.xlsx",
    sheet="Q1",
    rows=[["Mar", 1200], ["Apr", 1450]])
```

## Round-trip a sheet through CSV

```python
spreadsheet.export_csv(resource="data/sales.xlsx", sheet="Q1")
spreadsheet.import_csv(
    resource="data/sales.xlsx",
    imports=[{
        "sheet": "Q1Cleaned",
        "csvText": "month,total\nMar,1200\nApr,1450",
        "createIfMissing": True,
    }])
```

`import_csv` replaces a target sheet's contents in a single open/save cycle. Other sheets in the workbook are untouched.

## Export a sheet to a CSV file

Pass `destination` when the export is large or when only the file matters, not its contents in this turn:

```python
spreadsheet.export_csv(
    resource="data/sales.xlsx",
    sheet="Q1",
    destination="exports/sales_q1.csv")
```

The tool returns `{rowCount, columnCount, byteCount, destination}` instead of the CSV body.

## Multi-sheet scaffolding

```python
# 1. Rename the default Sheet1 to Q1
spreadsheet.rename_sheet(resource="data/sales.xlsx", sheet="Sheet1", new_name="Q1")
# 2. Add the rest of the sheets in one call
spreadsheet.add_sheets(resource="data/sales.xlsx", sheets=["Q2", "Q3", "Q4"])
# 3. Populate every sheet from CSV in one call
spreadsheet.import_csv(resource="data/sales.xlsx", imports=[
    {"sheet": "Q1", "csvText": "..."},
    {"sheet": "Q2", "csvText": "..."},
    {"sheet": "Q3", "csvText": "..."},
    {"sheet": "Q4", "csvText": "..."},
])
# 4. Apply formatting across all sheets in one call
spreadsheet.format_ranges(resource="data/sales.xlsx", edits=[
    {"sheet": "Q1", "range": "1", "format": {"textFormat": {"bold": True}}},
    {"sheet": "Q2", "range": "1", "format": {"textFormat": {"bold": True}}},
    {"sheet": "Q3", "range": "1", "format": {"textFormat": {"bold": True}}},
    {"sheet": "Q4", "range": "1", "format": {"textFormat": {"bold": True}}},
])
```

The find -> decide -> write pattern keeps any substring-match risk under the agent's review:

```python
matches = spreadsheet.find(resource="data/sales.xlsx", find="TODO")
# Inspect matches, then submit targeted edits via write_cells.
```
