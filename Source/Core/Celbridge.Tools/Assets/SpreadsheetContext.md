# Celbridge Spreadsheet Tools

The `spreadsheet_*` tools read, query, and modify `.xlsx` workbooks directly,
without going through the SpreadJS editor. Call these tools when you need to
inspect or change cell data, sheet structure, or CSV interchange. The SpreadJS
editor is the human-facing surface for the same `.xlsx` files; the two views
share state through disk.

Call `spreadsheet_get_context` once before doing non-trivial spreadsheet work.

## Creating new workbooks

Create a new `.xlsx` workbook with `explorer_create_file`, passing a resource
key with the `.xlsx` extension. Celbridge produces a real ClosedXML-backed
empty workbook with a default `Sheet1` worksheet — never write a zero-byte
file with `file_write` and expect it to open. To rename the initial sheet,
follow up with `spreadsheet_rename_sheet`.

## Cell addressing

Every range parameter uses A1 notation: `"A1"`, `"B2:D10"`. The sheet name is
always a separate parameter. **Do not** embed the sheet inside the range as
`"Sheet1!A1"` — that form is rejected with an error.

## JSON cell typing

Cells round-trip with their Excel type preserved:

| Cell type     | JSON representation                                  |
|---------------|------------------------------------------------------|
| Number        | JSON number                                          |
| Date/DateTime | ISO 8601 string (`"2026-05-02T00:00:00.0000000"`)    |
| Boolean       | JSON `true` / `false`                                |
| String        | JSON string                                          |
| Error         | JSON string with the `#` prefix (`"#DIV/0!"`)        |
| Blank         | JSON `null`                                          |

`spreadsheet_write_cells` accepts the same shapes on input. Strings that begin
with `=` are written as **text** by default. To write a formula, set
`isFormula: true` on the edit. Explicit beats sniffing.

## Formula writes require an explicit recalculate

ClosedXML does not recompute formulas on save; it marks affected cells dirty
and the next consumer (Excel, the SpreadJS editor) recalculates. That means a
`spreadsheet_read_sheet` issued immediately after a formula write returns
**stale or missing computed values**.

After every formula write, call `spreadsheet_recalculate` before reading
computed values back. This applies to `spreadsheet_write_cells` (when
`isFormula: true`), `spreadsheet_append_rows` (when a row contains a formula),
and `spreadsheet_from_csv` (CSVs typically do not contain formulas, but
recalculate if you imported one that does).

`spreadsheet_recalculate` is `O(workbook)` and runs across the whole file;
skip it when you only need the literal values you just wrote.

## Reading sheets

`spreadsheet_get_info` is the cheap first step: it returns sheet names, used
ranges, row and column counts, and any defined names. Use it before a large
read to avoid pulling in more than you need.

`spreadsheet_read_sheet` returns cell values from a sheet. Default is the
sheet's used range. Set `range` to read a sub-region (`"B2:D10"`). Set
`mode: "formulas"` to return formula text instead of computed values for
cells that contain a formula.

### Read paging

The default row limit is 1000. `offset` and `limit` page through large
sheets the same way `file_read` does:

- Read first 1000 rows: omit `offset` and `limit`.
- Read rows 1000–1999: `offset: 1000`.
- Read all rows in one call: `limit: 0` (use sparingly on large sheets).

The response always includes `totalRowCount` (total data rows in the requested
range, ignoring offset and limit), so you can decide whether to page.

### Headers mode

Set `headers: true` to treat the first row in the requested range as column
names. Each subsequent row is returned as an object keyed by header. Two rules
apply:

- Duplicate header strings get a numeric suffix: `name`, `name_2`, `name_3`.
- Empty header cells are replaced with `column_<letter>`: `column_A`,
  `column_C`.

Use headers mode when you want to address columns by name; leave it off (the
default) when you want positional row arrays.

## CSV interchange

`spreadsheet_to_csv` exports a sheet (or a sub-range of one) as RFC 4180 CSV
text. By default it returns the CSV inline so you can grep, summarise, or
feed it to another tool that takes inline text. If the export is large, set
`destination` to a resource key (e.g. `"data/sales_export.csv"`) and the tool
writes the file directly and returns a one-line summary instead of the body.
Prefer the `destination` form when the CSV will be the input to a follow-up
file operation, when it exceeds a few hundred rows, or when you do not need
to read the contents in the same turn.

`spreadsheet_from_csv` replaces the contents of a named sheet with CSV data.
The sheet is created if it does not yet exist. Other sheets in the workbook
are untouched. The CSV parser follows RFC 4180 strictly: comma delimiter,
double-quote quoting, no auto-detection of `;` or tab delimiters.

## Sheet lifecycle

- `spreadsheet_add_sheet` — appends a new empty sheet. Fails if a sheet of
  that name already exists.
- `spreadsheet_remove_sheet` — removes a sheet. Fails if it is the only
  sheet in the workbook.
- `spreadsheet_rename_sheet` — renames a sheet. Fails on name collision.

## Division of labour with the SpreadJS editor

The MCP tools cover **data, formulas, sheet lifecycle, and CSV interchange**.
The SpreadJS editor covers **cell formatting, conditional formatting, charts,
pivot tables, data validation, and styles**. The MCP tools do not author
presentation, but they preserve any presentation that already exists on cells
they do not touch — a `spreadsheet_write_cells` to `B3` does not strip styles
on `A1`.

If the user wants formatting or charts, they do it in the editor. If you need
cell data changed, use the MCP tools.

## Common workflows

### Inspect a workbook before reading

```
spreadsheet_get_info(resource: "data/sales.xlsx")
spreadsheet_read_sheet(resource: "data/sales.xlsx", sheet: "Q1", headers: true)
```

### Read-modify-write a single cell

```
spreadsheet_read_sheet(resource: "data/sales.xlsx", sheet: "Q1", range: "B3")
spreadsheet_write_cells(
  resource: "data/sales.xlsx",
  sheet: "Q1",
  edits: [{cell: "B3", value: 42}])
```

### Write a formula and read its computed value

```
spreadsheet_write_cells(
  resource: "data/sales.xlsx",
  sheet: "Q1",
  edits: [{cell: "C3", value: "=SUM(A1:A10)", isFormula: true}])
spreadsheet_recalculate(resource: "data/sales.xlsx")
spreadsheet_read_sheet(resource: "data/sales.xlsx", sheet: "Q1", range: "C3")
```

### Append rows to the end of a sheet

```
spreadsheet_append_rows(
  resource: "data/sales.xlsx",
  sheet: "Q1",
  rows: [["Mar", 1200], ["Apr", 1450]])
```

### Round-trip a sheet through CSV

```
spreadsheet_to_csv(resource: "data/sales.xlsx", sheet: "Q1")
spreadsheet_from_csv(
  resource: "data/sales.xlsx",
  sheet: "Q1Cleaned",
  csvText: "month,total\\nMar,1200\\nApr,1450",
  createIfMissing: true)
```

### Export a sheet to a CSV file

Use `destination` when the export is large or when you only need the file on
disk rather than the contents in this turn.

```
spreadsheet_to_csv(
  resource: "data/sales.xlsx",
  sheet: "Q1",
  destination: "exports/sales_q1.csv")
```

### Multi-sheet pattern

```
spreadsheet_add_sheet(resource: "data/sales.xlsx", sheet: "Q2")
spreadsheet_from_csv(resource: "data/sales.xlsx", sheet: "Q2", csvText: "...")
spreadsheet_rename_sheet(resource: "data/sales.xlsx", sheet: "Sheet1", newName: "Q1")
```
