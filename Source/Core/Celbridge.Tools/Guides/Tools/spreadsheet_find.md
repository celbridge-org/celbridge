# spreadsheet_find

Searches a workbook for cells whose text or formula expression contains a search string. Returns the list of matches without modifying the workbook.

Use this to identify cells to act on (e.g. before a targeted `spreadsheet_write_cells`) without slurping the whole sheet via `spreadsheet_read_sheet`.

## What is searched

- **Text-bearing cells** — searched against the cell's text.
- **Formula cells** — searched against the formula expression text (without the leading `=`). `SUM(A1:A10)` would match `"A1:A10"` or `"SUM"`.

Numeric, boolean, and date cells are skipped.

## Range scoping

- Empty `sheet` searches every worksheet; otherwise the named sheet only.
- `range` is only valid when `sheet` is also specified.
- Empty `range` searches the chosen sheet's entire used range.

## Response shape

Each match carries `sheet`, `cell`, `text`, and `isFormula`. For formula cells, `text` is the formula expression without the leading `=` (e.g. `"SUM(C2:F2)"` for the cell `=SUM(C2:F2)`).

`isFormula` lets the caller distinguish matches against formula text from matches against displayed values. Searching `"North"` would return both `A2` containing the literal string `"North"` (`isFormula: false`) and `B2` containing `=SUMIF(RawSales!B:B, "North", ...)` (`isFormula: true`) — the latter only matches because the formula expression contains `"North"` as a string literal argument, not because the cell's computed value matches.
