# Spreadsheet workflows

Common end-to-end task sequences. Examples use the Python proxy form; the MCP and JavaScript forms are equivalent (see `agent_instructions` for the proxy conventions).

## Inspect before reading

Always cheap, always the right first call. `get_info` returns sheet names, used ranges, row and column counts, frozen panes, and named ranges.

```python
spreadsheet.get_info(resource="data/sales.xlsx")
```

## Read-modify-write a single cell

```python
spreadsheet.read_sheet(resource="data/sales.xlsx", sheet="Q1", range="B3")
spreadsheet.write_cells(resource="data/sales.xlsx", sheet="Q1",
    edits=[{"cell": "B3", "value": 42}])
```

## Write a formula and read its computed value

Formula recalculation runs as part of the write, so the read sees the up-to-date value. See `spreadsheet_cell_typing` for the formula-vs-text rule.

```python
spreadsheet.write_cells(resource="data/sales.xlsx", sheet="Q1",
    edits=[{"cell": "C3", "value": "=SUM(A1:A10)", "isFormula": True}])
spreadsheet.read_sheet(resource="data/sales.xlsx", sheet="Q1", range="C3")
```

## Append rows

```python
spreadsheet.append_rows(resource="data/sales.xlsx", sheet="Q1",
    rows=[["Mar", 1200], ["Apr", 1450]])
```

## Round-trip a sheet through CSV

`import_csv` replaces a target sheet's contents in a single open/save cycle; other sheets are untouched.

```python
spreadsheet.export_csv(resource="data/sales.xlsx", sheet="Q1")
spreadsheet.import_csv(resource="data/sales.xlsx", imports=[
    {"sheet": "Q1Cleaned", "csvText": "month,total\nMar,1200\n", "createIfMissing": True}])
```

For a large export where the bytes are not needed in this turn, pass `destination`:

```python
spreadsheet.export_csv(resource="data/sales.xlsx", sheet="Q1",
    destination="exports/sales_q1.csv")
```

The tool then returns `{rowCount, columnCount, byteCount, destination}` instead of the body.

## Multi-sheet scaffolding

Rename the default `Sheet1`, then add the rest, then populate, then format — each step in one batched call.

```python
spreadsheet.rename_sheet(resource="data/sales.xlsx", sheet="Sheet1", new_name="Q1")
spreadsheet.add_sheets(resource="data/sales.xlsx", sheets=["Q2", "Q3", "Q4"])
spreadsheet.import_csv(resource="data/sales.xlsx", imports=[
    {"sheet": "Q1", "csvText": "..."},
    {"sheet": "Q2", "csvText": "..."}])
spreadsheet.format_ranges(resource="data/sales.xlsx", edits=[
    {"sheet": "Q1", "range": "1", "format": {"textFormat": {"bold": True}}},
    {"sheet": "Q2", "range": "1", "format": {"textFormat": {"bold": True}}}])
```

## Find before writing

The find-decide-write pattern keeps any substring-match risk under the agent's review.

```python
matches = spreadsheet.find(resource="data/sales.xlsx", find="TODO")
# Inspect matches, then submit targeted edits via write_cells.
```
