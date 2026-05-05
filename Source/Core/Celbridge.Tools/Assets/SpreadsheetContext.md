# Celbridge Spreadsheet Tools

The `spreadsheet_*` tools read, query, and modify `.xlsx` workbooks directly,
without going through the SpreadJS editor. Call these tools when you need to
inspect or change cell data, sheet structure, or CSV interchange. The SpreadJS
editor is the human-facing surface for the same `.xlsx` files. The two views
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

Every write tool recalculates the workbook's formulas as part of the save, so
a `spreadsheet_read_sheet` issued after a formula write returns the up-to-date
computed value.

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

Use headers mode when you want to address columns by name. Leave it off (the
default) when you want positional row arrays.

## CSV interchange

`spreadsheet_export_csv` exports a sheet (or a sub-range of one) as RFC 4180 CSV
text. By default it returns the CSV inline so you can grep, summarise, or
feed it to another tool that takes inline text. If the export is large, set
`destination` to a resource key (e.g. `"data/sales_export.csv"`) and the tool
writes the file directly and returns a JSON metadata object — `{rowCount,
columnCount, byteCount, destination}` — instead of the body. Prefer the
`destination` form when the CSV will be the input to a follow-up file
operation, when it exceeds a few hundred rows, or when you do not need to
read the contents in the same turn.

`spreadsheet_import_csv` replaces the contents of one or more named sheets with
CSV data in a single open/save cycle. Take an `imports` array where each entry
specifies a target `sheet`, the `csvText`, and an optional `createIfMissing`
flag (default false). Other sheets in the workbook are untouched. The CSV
parser follows RFC 4180 strictly: comma delimiter, double-quote quoting, no
auto-detection of `;` or tab delimiters.

```
spreadsheet_import_csv(
  resource: "data/sales.xlsx",
  imports: [
    {sheet: "Q1", csvText: "month,total\nJan,100\n", createIfMissing: true},
    {sheet: "Q2", csvText: "month,total\nApr,200\n", createIfMissing: true}
  ])
```

All rows in each CSV must have the same field count as that CSV's row 1. A row
with a different number of fields fails the whole batch with `CSV row N has X
fields, expected Y`. Pad shorter rows with empty fields (`,,`) if the data is
genuinely ragged.

Imports run in order. If any import fails the whole batch fails and nothing is
saved.

## Sheet lifecycle

- `spreadsheet_add_sheets` — appends one or more new empty sheets in a single
  call. Take a `sheets` array of names. Fails if any name collides with an
  existing sheet or with another name in the same batch. In that case nothing
  is saved.
- `spreadsheet_remove_sheet` — removes a sheet. Fails if it is the only
  sheet in the workbook.
- `spreadsheet_rename_sheet` — renames a sheet. Fails on name collision.
- `spreadsheet_move_sheet` — moves a sheet to a new 1-based tab position.
  Position 1 places the sheet first. The maximum is the current sheet count.
  Use `spreadsheet_get_info` to read each sheet's current `position` first.

```
spreadsheet_add_sheets(
  resource: "data/sales.xlsx",
  sheets: ["Q1", "Q2", "Q3", "Q4"])
```

To insert a new sheet at a specific position, append it with `add_sheets`
then call `move_sheet` to place it. For example, inserting "Summary" as the
first sheet:

```
spreadsheet_add_sheets(resource: "data/sales.xlsx", sheets: ["Summary"])
spreadsheet_move_sheet(resource: "data/sales.xlsx", sheet: "Summary", position: 1)
```

## Formatting

`spreadsheet_format_ranges` applies a batch of format edits to one workbook in
a single open/save cycle. Each edit specifies a target `sheet`, `range`, and
`format` spec. Edits may target different sheets in the same workbook. Only
the fields present in each edit's `format` are applied. Existing formatting
on cells the target does not cover is preserved. Edits run in order. If any
edit fails (bad colour, missing sheet, etc.) the whole batch fails and nothing
is saved.

### Target forms

Each edit's `range` may be:

| Range argument | Example | Applies to |
|---|---|---|
| A1 cell range | `"A1:C3"` | Those cells |
| Single cell | `"B2"` | That cell |
| Column letter | `"B"` | Entire column B |
| Column range | `"B:D"` | Entire columns B through D |
| Row number | `"3"` | Entire row 3 |
| Row range | `"3:5"` | Entire rows 3 through 5 |

### Format spec

Each edit's `format` is a JSON object. All fields are optional. Missing fields
are left unchanged on the target cells.

```json
{
  "textFormat": {
    "bold": true,
    "italic": false,
    "underline": false,
    "strikethrough": false,
    "fontFamily": "Calibri",
    "fontSize": 12,
    "foregroundColor": "#000000"
  },
  "backgroundColor": "#FFFF00",
  "borders": {
    "top":    { "style": "SOLID",  "color": "#000000" },
    "bottom": { "style": "DASHED", "color": "#888888" },
    "left":   { "style": "NONE" },
    "right":  { "style": "NONE" }
  },
  "horizontalAlignment": "CENTER",
  "verticalAlignment": "MIDDLE",
  "wrapText": true,
  "numberFormat": "#,##0.00",
  "columnWidth": 24,
  "rowHeight": 18,
  "autoFitColumns": true,
  "mergeRange": true
}
```

**Colors** are CSS hex strings (`#RRGGBB`).

**Border styles:** `SOLID` (thin line), `DASHED`, `DOTTED`, `DOUBLE`, `NONE`
(removes the border). ClosedXML `XLBorderStyleValues` enum names are also
accepted for finer control.

**Horizontal alignment:** `LEFT`, `CENTER`, `RIGHT`, `GENERAL`, `JUSTIFY`.

**Vertical alignment:** `TOP`, `MIDDLE`, `BOTTOM`.

**`columnWidth`** is in Excel character units, **not pixels**. The unit is
roughly the width of digit `0` in the workbook's default font (about 7px on a
standard zoom). The default column width is `8.43`. A typical text column is
`10`–`30`; a wide column with long content is `30`–`60`. Values above `100`
are almost always a mistake — if you find yourself reaching for `200`+, you
are probably thinking in pixels. Prefer `autoFitColumns: true` when you want
the column to fit its content. Applies to the columns covered by the target.

**`rowHeight`** is in points (1 point ≈ 1.33px at 100% zoom). The default row
height is `15`. A typical row is `12`–`30`. Applies to the rows covered by
the target.

**`autoFitColumns`** calls `AdjustToContents()` after any explicit
`columnWidth` is applied, so the column expands to fit its current contents.
This is usually the right answer for data tables — let Excel pick the width
rather than guessing one.

**`mergeRange`** when `true`, merges all cells in the target range into a
single cell after styles are applied. The top-left cell's value is preserved;
values in other cells of the range are lost. When `false`, any existing merge
covering the target range is unmerged. Only valid for A1 cell ranges (e.g.
`"A1:C1"`); column and row ranges are rejected. Common use is a section-header
band that spans several columns.

### Clearing and resetting

To remove formatting that was applied previously, omitting the field is "no
change". To express "clear" or "reset to the workbook default", use the
sentinel value for that field:

| Field | Sentinel | Effect |
|---|---|---|
| `backgroundColor` | `""` | clears the cell fill |
| `foregroundColor` | `""` | resets font colour to the workbook default |
| `borders.{side}.color` | `""` | resets that side's border colour to the workbook default |
| `borders.{side}.style` | `"NONE"` | removes that side's border |
| `fontFamily` | `""` | resets to the workbook default font |
| `fontSize` | `0` (or any non-positive number) | resets to the workbook default size |
| `columnWidth` | any negative number | resets to the workbook default column width |
| `rowHeight` | any negative number | resets to the workbook default row height |
| `mergeRange` | `false` | unmerges any merge covering the range |
| `bold`, `italic`, `underline`, `strikethrough`, `wrapText` | `false` | turns the toggle off |
| `horizontalAlignment` | `"GENERAL"` | resets horizontal alignment |
| `verticalAlignment` | `"BOTTOM"` | resets vertical alignment (Excel default) |

`spreadsheet_read_format` returns the same sentinels for the cells it reads,
so feeding the output of `read_format` back into `format_ranges` produces a
faithful format copy: a "no fill" source cell pasted onto a coloured destination
correctly clears the destination, rather than leaving the previous colour in
place.

### Common formatting workflows

A single batch can mix several common patterns across one or more sheets:

```
spreadsheet_format_ranges(
  resource: "data/sales.xlsx",
  edits: [
    # Bold header row with grey background on Q1
    {sheet: "Q1", range: "1",
     format: {"textFormat": {"bold": true}, "backgroundColor": "#D3D3D3"}},
    # Autofit data columns A through D on Q1
    {sheet: "Q1", range: "A:D",
     format: {"autoFitColumns": true}},
    # Currency number format on B2:B20 of Q1
    {sheet: "Q1", range: "B2:B20",
     format: {"numberFormat": "#,##0.00"}},
    # Box border around a range on Q1
    {sheet: "Q1", range: "A1:C5",
     format: {"borders": {
       "top":    {"style": "SOLID", "color": "#000000"},
       "bottom": {"style": "SOLID", "color": "#000000"},
       "left":   {"style": "SOLID", "color": "#000000"},
       "right":  {"style": "SOLID", "color": "#000000"}
     }}},
    # Apply the same bold header to Q2 in the same call
    {sheet: "Q2", range: "1",
     format: {"textFormat": {"bold": true}, "backgroundColor": "#D3D3D3"}}
  ])
```

For a single edit, use a one-element array. The plural shape applies to both
cases for consistency with `spreadsheet_write_cells`.

## Freezing panes

`spreadsheet_freeze_panes` keeps the first N rows and/or the first M columns of
a sheet visible while the rest of the sheet scrolls. The two axes are
independent: freeze rows only, columns only, or both at once. Each frozen band
is always anchored at the top-left, so you cannot freeze arbitrary rows or
columns elsewhere on the sheet. Most common use is freezing the header row of a
data table so column names stay on screen as the user scrolls down.

```
# Freeze the first row only
spreadsheet_freeze_panes(resource: "data/sales.xlsx", sheet: "Q1", rows: 1)

# Freeze the first row and first two columns
spreadsheet_freeze_panes(resource: "data/sales.xlsx", sheet: "Q1", rows: 1, columns: 2)

# Clear any existing freeze on the sheet
spreadsheet_freeze_panes(resource: "data/sales.xlsx", sheet: "Q1", rows: 0, columns: 0)
```

`rows` and `columns` default to 0. Either may be omitted to leave that axis
unfrozen.

## Setting the active view

`spreadsheet_set_active_view` controls what a user sees when they open the
workbook: which sheet is active, the cell selection on that sheet, and the
scroll position. Useful at the end of an authoring workflow ("leave the user
looking at the Summary sheet with cursor at A1") or when surfacing a result
for review ("select the row I just changed").

```
# Make Summary the active sheet, cursor on A1
spreadsheet_set_active_view(
  resource: "data/sales.xlsx",
  sheet: "Summary",
  range: "A1")

# Select a result row on Q1 and the user lands there on open
spreadsheet_set_active_view(
  resource: "data/sales.xlsx",
  sheet: "Q1",
  range: "B50:F50")

# Show rows 30-60 with row 50 selected (scroll explicitly)
spreadsheet_set_active_view(
  resource: "data/sales.xlsx",
  sheet: "Q1",
  range: "B50",
  topLeftCell: "A30")
```

The `sheet` parameter is required and is always made active. `range` and
`topLeftCell` are optional. An empty value leaves that aspect of the sheet's
view unchanged. For most "find this content" use cases, setting `range`
alone is enough — Excel auto-scrolls the viewport to make the active cell
visible on open. Use `topLeftCell` when you want to control the surrounding
context independently of the selection.

`topLeftCell` interacts with frozen panes: if the sheet has frozen rows or
columns, the scroll origin sits below or to the right of the freeze, so a
`topLeftCell` address inside the frozen band is silently clamped.

### Behaviour while the workbook is open

If the workbook is open in the spreadsheet editor when this tool runs, the
new view state is applied through the editor's normal external-reload path
— the document tab stays open and the change is visible immediately. The
editor's in-memory undo history is cleared by the reload (the workbook is
re-imported from disk), but the document tab does not close. If the
workbook is not open, the new view state is honoured the next time the user
opens it.

## Reading formatting

`spreadsheet_read_format` returns the format spec for each cell in a range as a
2D array of `SpreadsheetFormatSpec` objects — the same shape accepted by
`spreadsheet_format_ranges`. Only non-default properties appear in each cell's
spec. A completely unformatted cell includes only its effective font name and
size.

```
spreadsheet_read_format(resource: "data/report.xlsx", sheet: "Template", range: "A1:E1")
```

Returns:
```json
{
  "range": "Template!A1:E1",
  "rows": [
    [
      {"textFormat": {"bold": true, "fontFamily": "Calibri", "fontSize": 14}, "backgroundColor": "#4472C4"},
      {"textFormat": {"fontFamily": "Calibri", "fontSize": 11}},
      ...
    ]
  ]
}
```

**`range`** must be A1 cell notation (e.g. `"A1:C3"` or `"B2"`). Empty string
reads the used range. Column and row letter ranges (`"A:C"`, `"1:3"`) are not
supported — use the used-range dimensions from `spreadsheet_get_info` to
construct an explicit cell range if needed.

### Copy-format workflow

To copy the header-row format from one sheet to another:

```
# 1. Read source formatting
spreadsheet_read_format(resource: "data/report.xlsx", sheet: "Template", range: "A1:E1")
# 2. Apply each distinct format spec to the target range as one batch
spreadsheet_format_ranges(
  resource: "data/report.xlsx",
  edits: [{sheet: "Output", range: "A1:E1", format: <spec from rows[0][0]>}])
```

When all header cells share the same spec (common case), one edit covers the
whole range. When cells differ, include one edit per distinct spec in the same
batch, narrowing the range of each edit to just the cells that share it.

### Division of labour with the SpreadJS editor

The MCP formatting tool covers **common cell styling agents reach for**: font,
fill, borders, alignment, number format, column width, and autofit. The
SpreadJS editor covers **advanced presentation work**: conditional formatting,
charts, pivot tables, data validation, named styles, and themes.

## Division of labour with the SpreadJS editor

The MCP tools cover **data, formulas, sheet lifecycle, CSV interchange, and
common cell formatting**. The SpreadJS editor covers **conditional formatting,
charts, pivot tables, data validation, named styles, and themes**. The MCP
tools preserve any presentation that already exists on cells they do not touch
— a `spreadsheet_write_cells` to `B3` does not strip styles on `A1`, and a
`spreadsheet_format_ranges` on `A1:C1` does not touch `D1`.

For advanced styling not covered by `spreadsheet_format_ranges`, open the
workbook in the SpreadJS editor.

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
spreadsheet_export_csv(resource: "data/sales.xlsx", sheet: "Q1")
spreadsheet_import_csv(
  resource: "data/sales.xlsx",
  imports: [{sheet: "Q1Cleaned", csvText: "month,total\\nMar,1200\\nApr,1450", createIfMissing: true}])
```

### Export a sheet to a CSV file

Use `destination` when the export is large or when you only need the file on
disk rather than the contents in this turn.

```
spreadsheet_export_csv(
  resource: "data/sales.xlsx",
  sheet: "Q1",
  destination: "exports/sales_q1.csv")
```

### Multi-sheet scaffolding

```
# 1. Rename the default Sheet1 to Q1
spreadsheet_rename_sheet(resource: "data/sales.xlsx", sheet: "Sheet1", newName: "Q1")
# 2. Add the rest of the sheets in one call
spreadsheet_add_sheets(resource: "data/sales.xlsx", sheets: ["Q2", "Q3", "Q4"])
# 3. Populate every sheet from CSV in one call
spreadsheet_import_csv(resource: "data/sales.xlsx", imports: [
  {sheet: "Q1", csvText: "..."},
  {sheet: "Q2", csvText: "..."},
  {sheet: "Q3", csvText: "..."},
  {sheet: "Q4", csvText: "..."}
])
# 4. Apply formatting across all sheets in one call
spreadsheet_format_ranges(resource: "data/sales.xlsx", edits: [
  {sheet: "Q1", range: "1", format: {"textFormat": {"bold": true}}},
  {sheet: "Q2", range: "1", format: {"textFormat": {"bold": true}}},
  {sheet: "Q3", range: "1", format: {"textFormat": {"bold": true}}},
  {sheet: "Q4", range: "1", format: {"textFormat": {"bold": true}}}
])
```
